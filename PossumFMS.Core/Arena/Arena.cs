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

    public TimeSpan AutoDuration { get; private set; } = TimeSpan.FromSeconds(20);
    public TimeSpan AutoToTeleopTransitionDuration { get; private set; } = TimeSpan.FromSeconds(3);
    public TimeSpan TeleopDuration { get; private set; } = TimeSpan.FromSeconds(140);

    private readonly Stopwatch _phaseTimer = new();
    private TimeSpan _phaseDuration;

    // ── State ──────────────────────────────────────────────────────────────────

    public MatchPhase Phase { get; private set; } = MatchPhase.Idle;
    public bool FreePracticeEnabled { get; private set; }

    // ── Match metadata (encoded in every control packet) ───────────────────────

    public MatchType MatchType   { get; set; } = MatchType.Test;
    public int       MatchNumber { get; set; } = 1;
    public int       MatchRepeat { get; set; } = 0;
    public string    MatchId     { get; private set; } = Guid.NewGuid().ToString();

    /// <summary>Time remaining in the current phase. Zero when Idle or PostMatch.</summary>
    public TimeSpan TimeRemaining =>
        Phase is MatchPhase.Idle or MatchPhase.PostMatch
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Max(0, (_phaseDuration - _phaseTimer.Elapsed).Ticks));

    /// <summary>
    /// True only in phases where robots should be enabled by DriverStationManager.
    /// </summary>
    public bool IsMatchRunning => Phase is MatchPhase.Auto or MatchPhase.Teleop;

    /// <summary>
    /// True while a match is in progress, including the Auto→Teleop transition.
    /// </summary>
    public bool IsMatchInProgress => Phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition or MatchPhase.Teleop;

    /// <summary>True if the last match ended via AbortMatch rather than running to completion.</summary>
    public bool WasAborted { get; private set; }

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
        if (FreePracticeEnabled)
            throw new InvalidOperationException("Cannot start pre-match while Free Practice is enabled.");

        if (IsMatchInProgress)
            throw new InvalidOperationException("Cannot start pre-match while a match is running.");

        MatchId = Guid.NewGuid().ToString();
        WasAborted = false;
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
        if (!IsMatchInProgress)
            throw new InvalidOperationException("No match is running.");

        WasAborted = true;
        TransitionTo(MatchPhase.PostMatch, TimeSpan.Zero);
    }

    public void ClearMatch()
    {
        if (IsMatchInProgress)
            throw new InvalidOperationException("Cannot clear match while a match is running.");

        WasAborted = false;
        TransitionTo(MatchPhase.Idle, TimeSpan.Zero);
    }

    public void SetFreePracticeEnabled(bool enabled)
    {
        if (Phase != MatchPhase.Idle)
            throw new InvalidOperationException("Free Practice can only be changed while the arena is idle.");

        FreePracticeEnabled = enabled;
    }

    public void SetMatchDurations(
        TimeSpan autoDuration,
        TimeSpan autoToTeleopTransitionDuration,
        TimeSpan teleopDuration)
    {
        if (Phase != MatchPhase.Idle)
            throw new InvalidOperationException("Match durations can only be changed while the arena is idle.");

        if (autoDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoDuration), "Auto duration cannot be negative.");

        if (autoToTeleopTransitionDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoToTeleopTransitionDuration), "Auto-to-Teleop transition duration cannot be negative.");

        if (teleopDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(teleopDuration), "Teleop duration cannot be negative.");

        AutoDuration = autoDuration;
        AutoToTeleopTransitionDuration = autoToTeleopTransitionDuration;
        TeleopDuration = teleopDuration;
    }

    /// <summary>
    /// Called by the DriverStationManager on each tick to advance phase transitions
    /// (e.g. Auto → Teleop → Over) based on elapsed time.
    /// </summary>
    public void Tick()
    {
        while (true)
        {
            switch (Phase)
            {
                case MatchPhase.Auto when TimeRemaining == TimeSpan.Zero:
                    TransitionTo(MatchPhase.AutoToTeleopTransition, AutoToTeleopTransitionDuration);
                    continue;
                case MatchPhase.AutoToTeleopTransition when TimeRemaining == TimeSpan.Zero:
                    TransitionTo(MatchPhase.Teleop, TeleopDuration);
                    continue;
                case MatchPhase.Teleop when TimeRemaining == TimeSpan.Zero:
                    TransitionTo(MatchPhase.PostMatch, TimeSpan.Zero);
                    continue;
            }

            break;
        }
    }

    // ── E-Stop ─────────────────────────────────────────────────────────────────

    public void TriggerArenaEstop()
    {
        ArenaEstop = true;

        if (IsMatchInProgress)
            AbortMatch();
    }

    /// <summary>Clears the arena e-stop. Only valid when match is not running.</summary>
    public void ResetArenaEstop()
    {
        if (IsMatchInProgress)
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
