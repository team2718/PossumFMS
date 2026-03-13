using System.Diagnostics;

namespace PossumFMS.Core.Arena;

/// <summary>
/// Central match/field state machine. Holds authoritative match state and drives
/// transitions between match phases. DriverStationManager reads from this to build
/// control packets; FieldHardwareManager reads from this to command field devices.
/// </summary>
public sealed class Arena
{
    // ── Match timing ───────────────────────────────────────────────────────────

    private static readonly TimeSpan AutoDuration    = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AutoToTeleopTransitionDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TeleopDuration  = TimeSpan.FromSeconds(140);

    private readonly Stopwatch _phaseTimer = new();
    private TimeSpan _phaseDuration;

    // ── State ──────────────────────────────────────────────────────────────────

    public MatchPhase Phase { get; private set; } = MatchPhase.Idle;

    // ── Match metadata (encoded in every control packet) ───────────────────────

    public MatchType MatchType   { get; set; } = MatchType.None;
    public int       MatchNumber { get; set; } = 1;
    public int       MatchRepeat { get; set; } = 1;

    /// <summary>Time remaining in the current phase. Zero when Idle or Over.</summary>
    public TimeSpan TimeRemaining =>
        Phase is MatchPhase.Idle or MatchPhase.PostMatch
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Max(0, (_phaseDuration - _phaseTimer.Elapsed).Ticks));

    public bool IsMatchRunning => Phase is MatchPhase.Auto or MatchPhase.Teleop;

    // ── Arena-wide stops ───────────────────────────────────────────────────────

    /// <summary>
    /// When true all robots are e-stopped regardless of per-station flags.
    /// Requires reset and match restart to clear.
    /// </summary>
    public bool ArenaEstop { get; private set; }

    // ── Game data ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Game-specific message forwarded to robots via DS (max ~64 bytes).
    /// Set by game logic; broadcast to all DSes by DriverStationManager.
    /// </summary>
    public string GameData { get; private set; } = string.Empty;

    /// <summary>Fired whenever game data changes. Payload is the new value.</summary>
    public event Action<string>? GameDataChanged;

    /// <summary>
    /// Updates game data and notifies listeners. Pass an empty string to clear.
    /// </summary>
    public void SetGameData(string data)
    {
        GameData = data;
        GameDataChanged?.Invoke(data);
    }

    // ── Events ─────────────────────────────────────────────────────────────────

    public event Action<MatchPhase>? PhaseChanged;

    // ── Match control ──────────────────────────────────────────────────────────

    public void StartPreMatch()
    {
        if (IsMatchRunning)
            throw new InvalidOperationException("Cannot start pre-match while a match is running.");

        TransitionTo(MatchPhase.PreMatch, TimeSpan.Zero);
    }

    public void StartMatch()
    {
        if (Phase != MatchPhase.PreMatch)
            throw new InvalidOperationException($"Cannot start match in phase {Phase}.");

        TransitionTo(MatchPhase.Auto, AutoDuration);
    }

    public void AbortMatch()
    {
        if (!IsMatchRunning)
            throw new InvalidOperationException("No match is running.");

        TransitionTo(MatchPhase.PostMatch, TimeSpan.Zero);
    }

    public void ClearMatch()
    {
        if (IsMatchRunning)
            throw new InvalidOperationException("Cannot clear match while a match is running.");

        TransitionTo(MatchPhase.Idle, TimeSpan.Zero);
    }

    /// <summary>
    /// Called by one of the workers on each tick to advance phase transitions
    /// (e.g. Auto → Teleop → Over) based on elapsed time.
    /// </summary>
    public void Tick()
    {
        switch (Phase)
        {
            case MatchPhase.Auto when TimeRemaining == TimeSpan.Zero:
                TransitionTo(MatchPhase.AutoToTeleopTransition, AutoToTeleopTransitionDuration);
                break;
            case MatchPhase.AutoToTeleopTransition when TimeRemaining == TimeSpan.Zero:
                TransitionTo(MatchPhase.Teleop, TeleopDuration);
                break;
            case MatchPhase.Teleop when TimeRemaining == TimeSpan.Zero:
                TransitionTo(MatchPhase.PostMatch, TimeSpan.Zero);
                break;
        }
    }

    // ── E-Stop ─────────────────────────────────────────────────────────────────

    public void TriggerArenaEstop()
    {
        ArenaEstop = true;
        // Individual DS e-stops are handled on the DriverStationConnection.
    }

    /// <summary>Clears the arena e-stop. Only valid when match is not running.</summary>
    public void ResetArenaEstop()
    {
        if (IsMatchRunning)
            throw new InvalidOperationException("Cannot reset e-stop while match is running.");

        ArenaEstop = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void TransitionTo(MatchPhase next, TimeSpan duration)
    {
        Phase = next;
        _phaseDuration = duration;
        _phaseTimer.Restart();
        PhaseChanged?.Invoke(next);
    }
}
