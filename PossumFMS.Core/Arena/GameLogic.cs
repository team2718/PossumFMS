namespace PossumFMS.Core.Arena;

/// <summary>
/// Owns per-match scoring state and applies REBUILT 2026 game-specific rules,
/// including the Shift 1–4 hub active/inactive system (Table 6-3).
/// </summary>
public sealed class GameLogic
{
    private readonly Arena _arena;
    private static readonly TimeSpan HubInactiveGracePeriod = TimeSpan.FromSeconds(3);
    private AllianceColor? _shiftAutoWinnerAlliance;

    public AllianceColor? ShiftAutoWinnerAlliance => _shiftAutoWinnerAlliance;

    public AllianceScore RedScore  { get; } = new();
    public AllianceScore BlueScore { get; } = new();
    public event Action? ScoreChanged;

    public GameLogic(Arena arena)
    {
        _arena = arena;
        _arena.PhaseChanged += OnPhaseChanged;
    }

    // ── Teleop period ──────────────────────────────────────────────────────────

    /// <summary>
    /// Current sub-period within Teleop based on time remaining.
    /// Derived from Table 6-3. Returns <see cref="TeleopPeriod.NotStarted"/> outside of Teleop.
    /// </summary>
    public TeleopPeriod CurrentTeleopPeriod
    {
        get
        {
            if (_arena.Phase != MatchPhase.Teleop)
                return TeleopPeriod.NotStarted;

            // Teleop counts down from 140 s. Boundaries correspond to the Table 6-3 timer values.
            return _arena.TimeRemaining.TotalSeconds switch
            {
                > 130 => TeleopPeriod.TransitionShift, // 2:20 – 2:10
                > 105 => TeleopPeriod.Shift1,          // 2:10 – 1:45
                > 80  => TeleopPeriod.Shift2,          // 1:45 – 1:20
                > 55  => TeleopPeriod.Shift3,          // 1:20 – 0:55
                > 30  => TeleopPeriod.Shift4,          // 0:55 – 0:30
                _     => TeleopPeriod.EndGame,         // 0:30 – 0:00
            };
        }
    }

    // ── Hub active ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the hub for <paramref name="alliance"/> is currently active and may score fuel.
    /// </summary>
    public bool IsHubActive(AllianceColor alliance)
    {
        // Both hubs always active during Auto and the brief transition before Teleop.
        if (_arena.Phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition)
            return true;

        if (_arena.Phase != MatchPhase.Teleop)
            return false;

        if (IsHubStrictlyActive(alliance))
            return true;

        // Grace window: keep counting for 3 seconds after a hub just became inactive.
        return GetElapsedInCurrentTeleopPeriod() <= HubInactiveGracePeriod;
    }

    public bool IsHubStrictlyActive(AllianceColor alliance)
    {
        if (_arena.Phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition)
            return true;

        if (_arena.Phase != MatchPhase.Teleop)
            return false;

        return IsHubStrictlyActiveForPeriod(alliance, CurrentTeleopPeriod);
    }

    public bool IsHubAboutToBecomeInactive(AllianceColor alliance, TimeSpan within)
    {
        if (_arena.Phase != MatchPhase.Teleop)
            return false;

        if (!IsHubStrictlyActive(alliance))
            return false;

        if (within < TimeSpan.Zero)
            return false;

        var currentPeriod = CurrentTeleopPeriod;
        var timeRemainingInPeriod = GetTimeRemainingInCurrentTeleopPeriod(currentPeriod);
        if (timeRemainingInPeriod > within)
            return false;

        var nextPeriod = GetNextTeleopPeriod(currentPeriod);
        if (nextPeriod == currentPeriod)
            return false;

        return !IsHubStrictlyActiveForPeriod(alliance, nextPeriod);
    }

    private bool IsHubStrictlyActiveForPeriod(AllianceColor alliance, TeleopPeriod period)
    {
        if (_arena.Phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition)
            return true;

        if (_arena.Phase != MatchPhase.Teleop)
            return false;

        return period switch
        {
            TeleopPeriod.TransitionShift or TeleopPeriod.EndGame => true,

            TeleopPeriod.Shift1 or TeleopPeriod.Shift3 => alliance != _shiftAutoWinnerAlliance,
            TeleopPeriod.Shift2 or TeleopPeriod.Shift4 => alliance == _shiftAutoWinnerAlliance,

            _ => false,
        };
    }

    private TeleopPeriod GetNextTeleopPeriod(TeleopPeriod period)
    {
        return period switch
        {
            TeleopPeriod.TransitionShift => TeleopPeriod.Shift1,
            TeleopPeriod.Shift1 => TeleopPeriod.Shift2,
            TeleopPeriod.Shift2 => TeleopPeriod.Shift3,
            TeleopPeriod.Shift3 => TeleopPeriod.Shift4,
            TeleopPeriod.Shift4 => TeleopPeriod.EndGame,
            _ => period,
        };
    }

    private TimeSpan GetTimeRemainingInCurrentTeleopPeriod(TeleopPeriod period)
    {
        if (_arena.Phase != MatchPhase.Teleop)
            return TimeSpan.Zero;

        var periodEndSeconds = period switch
        {
            TeleopPeriod.TransitionShift => 130,
            TeleopPeriod.Shift1 => 105,
            TeleopPeriod.Shift2 => 80,
            TeleopPeriod.Shift3 => 55,
            TeleopPeriod.Shift4 => 30,
            TeleopPeriod.EndGame => 0,
            _ => (int)_arena.TimeRemaining.TotalSeconds,
        };

        var remainingSeconds = Math.Max(0, _arena.TimeRemaining.TotalSeconds - periodEndSeconds);
        return TimeSpan.FromSeconds(remainingSeconds);
    }

    private TimeSpan GetElapsedInCurrentTeleopPeriod()
    {
        if (_arena.Phase != MatchPhase.Teleop)
            return TimeSpan.Zero;

        var timeRemaining = _arena.TimeRemaining.TotalSeconds;

        var elapsedSeconds = CurrentTeleopPeriod switch
        {
            TeleopPeriod.TransitionShift => 140 - timeRemaining,
            TeleopPeriod.Shift1 => 130 - timeRemaining,
            TeleopPeriod.Shift2 => 105 - timeRemaining,
            TeleopPeriod.Shift3 => 80 - timeRemaining,
            TeleopPeriod.Shift4 => 55 - timeRemaining,
            TeleopPeriod.EndGame => 30 - timeRemaining,
            _ => 0,
        };

        return TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds));
    }

    // ── Phase transitions ──────────────────────────────────────────────────────

    private void OnPhaseChanged(MatchPhase phase)
    {
        switch (phase)
        {
            case MatchPhase.PreMatch:
                RedScore.Reset();
                BlueScore.Reset();
                _shiftAutoWinnerAlliance = null;
                _arena.SetGameData(string.Empty);
                ScoreChanged?.Invoke();
                break;

            case MatchPhase.Teleop:
                // The alliance that scored more fuel in Auto has their hub inactive in Shift 1.
                // A tie is broken randomly.
                _shiftAutoWinnerAlliance =
                    RedScore.AutoFuelPoints > BlueScore.AutoFuelPoints ? AllianceColor.Red
                    : BlueScore.AutoFuelPoints > RedScore.AutoFuelPoints ? AllianceColor.Blue
                    : (new Random().Next(2) == 0 ? AllianceColor.Red : AllianceColor.Blue);

                // Encode into game data for driver station packets.
                _arena.SetGameData(_shiftAutoWinnerAlliance == AllianceColor.Red ? "R" : "B");
                break;
        }
    }

    // ── Scoring ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records fuel scored by <paramref name="alliance"/>'s hub.
    /// Silently no-ops when the hub is inactive for the current period.
    /// </summary>
    public void ScoreFuel(AllianceColor alliance, int count)
    {
        if (count <= 0) return;
        if (!IsHubActive(alliance)) return;

        var score = alliance == AllianceColor.Red ? RedScore : BlueScore;

        if (_arena.Phase == MatchPhase.Auto)
            score.AutoFuelPoints += count;
        else if (_arena.Phase == MatchPhase.Teleop)
            score.TeleopFuelPoints += count;
        else
            return;

        ScoreChanged?.Invoke();
    }
}
