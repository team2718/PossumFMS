using System.Diagnostics;

namespace PossumFMS.Core.Arena;

/// <summary>
/// Central match/field state machine. Holds authoritative match state and drives
/// transitions between match phases. DriverStationManager reads from this to build
/// control packets; FieldHardwareManager reads from this to command field devices.
/// </summary>
public sealed class Arena
{
    private readonly object _stateLock = new();

    // ── Match timing ───────────────────────────────────────────────────────────

    public TimeSpan AutoDuration
    {
        get { lock (_stateLock) return _autoDuration; }
        private set { lock (_stateLock) _autoDuration = value; }
    }
    private TimeSpan _autoDuration = TimeSpan.FromSeconds(20);

    public TimeSpan AutoToTeleopTransitionDuration
    {
        get { lock (_stateLock) return _autoToTeleopTransitionDuration; }
        private set { lock (_stateLock) _autoToTeleopTransitionDuration = value; }
    }
    private TimeSpan _autoToTeleopTransitionDuration = TimeSpan.FromSeconds(3);

    public TimeSpan TeleopDuration
    {
        get { lock (_stateLock) return _teleopDuration; }
        private set { lock (_stateLock) _teleopDuration = value; }
    }
    private TimeSpan _teleopDuration = TimeSpan.FromSeconds(140);

    private readonly Stopwatch _phaseTimer = new();
    private TimeSpan _phaseDuration;

    // ── State ──────────────────────────────────────────────────────────────────

    public MatchPhase Phase
    {
        get { lock (_stateLock) return _phase; }
        private set { lock (_stateLock) _phase = value; }
    }
    private MatchPhase _phase = MatchPhase.Idle;

    public bool FreePracticeEnabled
    {
        get { lock (_stateLock) return _freePracticeEnabled; }
        private set { lock (_stateLock) _freePracticeEnabled = value; }
    }
    private bool _freePracticeEnabled;

    public bool RequireFieldEstopForMatchStart
    {
        get { lock (_stateLock) return _requireFieldEstopForMatchStart; }
        private set { lock (_stateLock) _requireFieldEstopForMatchStart = value; }
    }
    private bool _requireFieldEstopForMatchStart = true;


    // ── Match metadata (encoded in every control packet) ───────────────────────

    public MatchType MatchType
    {
        get { lock (_stateLock) return _matchType; }
        set { lock (_stateLock) _matchType = value; }
    }
    private MatchType _matchType = MatchType.Test;

    public int MatchNumber
    {
        get { lock (_stateLock) return _matchNumber; }
        set { lock (_stateLock) _matchNumber = value; }
    }
    private int _matchNumber = 1;

    public int MatchRepeat
    {
        get { lock (_stateLock) return _matchRepeat; }
        set { lock (_stateLock) _matchRepeat = value; }
    }
    private int _matchRepeat = 0;

    public string MatchId
    {
        get { lock (_stateLock) return _matchId; }
        private set { lock (_stateLock) _matchId = value; }
    }
    private string _matchId = Guid.NewGuid().ToString();

    /// <summary>Time remaining in the current phase. Zero when Idle or PostMatch.</summary>
    public TimeSpan TimeRemaining
    {
        get
        {
            lock (_stateLock)
            {
                return _phase is MatchPhase.Idle or MatchPhase.PostMatch
                    ? TimeSpan.Zero
                    : TimeSpan.FromTicks(Math.Max(0, (_phaseDuration - _phaseTimer.Elapsed).Ticks));
            }
        }
    }

    /// <summary>
    /// True only in phases where robots should be enabled by DriverStationManager.
    /// </summary>
    public bool IsMatchRunning
    {
        get
        {
            lock (_stateLock)
                return _phase is MatchPhase.Auto or MatchPhase.Teleop;
        }
    }

    /// <summary>
    /// True while a match is in progress, including the Auto→Teleop transition.
    /// </summary>
    public bool IsMatchInProgress
    {
        get
        {
            lock (_stateLock)
                return _phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition or MatchPhase.Teleop;
        }
    }

    /// <summary>True if the last match ended via AbortMatch rather than running to completion.</summary>
    public bool WasAborted
    {
        get { lock (_stateLock) return _wasAborted; }
        private set { lock (_stateLock) _wasAborted = value; }
    }
    private bool _wasAborted;

    // ── Arena-wide stops ───────────────────────────────────────────────────────

    /// <summary>
    /// When true all robots are e-stopped regardless of per-station flags.
    /// Requires reset and match restart to clear.
    /// </summary>
    public bool ArenaEstop
    {
        get { lock (_stateLock) return _arenaEstop; }
        private set { lock (_stateLock) _arenaEstop = value; }
    }
    private bool _arenaEstop;

    // ── Game data ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Game-specific message forwarded to robots via DS (max ~64 bytes).
    /// Set by game logic; broadcast to all DSes by DriverStationManager.
    /// </summary>
    public string GameData
    {
        get { lock (_stateLock) return _gameData; }
        private set { lock (_stateLock) _gameData = value; }
    }
    private string _gameData = string.Empty;

    /// <summary>Fired whenever game data changes. Payload is the new value.</summary>
    public event Action<string>? GameDataChanged;

    /// <summary>
    /// Updates game data and notifies listeners. Pass an empty string to clear.
    /// </summary>
    public void SetGameData(string data)
    {
        lock (_stateLock)
        {
            _gameData = data;
        }
        GameDataChanged?.Invoke(data);
    }

    // ── Events ─────────────────────────────────────────────────────────────────

    public event Action<MatchPhase>? PhaseChanged;

    // ── Match control ──────────────────────────────────────────────────────────

    public void StartPreMatch()
    {
        MatchPhase next;
        lock (_stateLock)
        {
            if (_freePracticeEnabled)
                throw new InvalidOperationException("Cannot start pre-match while Free Practice is enabled.");

            if (_phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition or MatchPhase.Teleop)
                throw new InvalidOperationException("Cannot start pre-match while a match is running.");

            _matchId = Guid.NewGuid().ToString();
            _wasAborted = false;
            next = TransitionToLocked(MatchPhase.PreMatch, TimeSpan.Zero);
        }
        PhaseChanged?.Invoke(next);
    }

    public void StartMatch()
    {
        MatchPhase next;
        lock (_stateLock)
        {
            if (_phase != MatchPhase.PreMatch)
                throw new InvalidOperationException($"Cannot start match in phase {_phase}.");

            next = TransitionToLocked(MatchPhase.Auto, _autoDuration);
        }
        PhaseChanged?.Invoke(next);
    }

    public void AbortMatch()
    {
        MatchPhase next;
        lock (_stateLock)
        {
            if (_phase is not (MatchPhase.Auto or MatchPhase.AutoToTeleopTransition or MatchPhase.Teleop))
                throw new InvalidOperationException("No match is running.");

            _wasAborted = true;
            next = TransitionToLocked(MatchPhase.PostMatch, TimeSpan.Zero);
        }
        PhaseChanged?.Invoke(next);
    }

    public void ClearMatch()
    {
        MatchPhase next;
        lock (_stateLock)
        {
            if (_phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition or MatchPhase.Teleop)
                throw new InvalidOperationException("Cannot clear match while a match is running.");

            _wasAborted = false;
            next = TransitionToLocked(MatchPhase.Idle, TimeSpan.Zero);
        }
        PhaseChanged?.Invoke(next);
    }

    public void SetFreePracticeEnabled(bool enabled)
    {
        lock (_stateLock)
        {
            if (_phase != MatchPhase.Idle)
                throw new InvalidOperationException("Free Practice can only be changed while the arena is idle.");

            _freePracticeEnabled = enabled;
        }
    }

    public void SetRequireFieldEstopForMatchStart(bool required)
    {
        lock (_stateLock)
        {
            if (_phase != MatchPhase.Idle)
                throw new InvalidOperationException("Field E-Stop requirement can only be changed while the arena is idle.");

            _requireFieldEstopForMatchStart = required;
        }
    }


    public void SetMatchDurations(
        TimeSpan autoDuration,
        TimeSpan autoToTeleopTransitionDuration,
        TimeSpan teleopDuration)
    {
        lock (_stateLock)
        {
            if (_phase != MatchPhase.Idle)
                throw new InvalidOperationException("Match durations can only be changed while the arena is idle.");

            if (autoDuration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(autoDuration), "Auto duration cannot be negative.");

            if (autoToTeleopTransitionDuration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(autoToTeleopTransitionDuration), "Auto-to-Teleop transition duration cannot be negative.");

            if (teleopDuration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(teleopDuration), "Teleop duration cannot be negative.");

            _autoDuration = autoDuration;
            _autoToTeleopTransitionDuration = autoToTeleopTransitionDuration;
            _teleopDuration = teleopDuration;
        }
    }

    /// <summary>
    /// Called by the DriverStationManager on each tick to advance phase transitions
    /// (e.g. Auto → Teleop → Over) based on elapsed time.
    /// </summary>
    public void Tick()
    {
        while (true)
        {
            MatchPhase? nextPhase = null;
            lock (_stateLock)
            {
                var remaining = _phase is MatchPhase.Idle or MatchPhase.PostMatch
                    ? TimeSpan.Zero
                    : TimeSpan.FromTicks(Math.Max(0, (_phaseDuration - _phaseTimer.Elapsed).Ticks));

                switch (_phase)
                {
                    case MatchPhase.Auto when remaining == TimeSpan.Zero:
                        nextPhase = TransitionToLocked(MatchPhase.AutoToTeleopTransition, _autoToTeleopTransitionDuration);
                        break;
                    case MatchPhase.AutoToTeleopTransition when remaining == TimeSpan.Zero:
                        nextPhase = TransitionToLocked(MatchPhase.Teleop, _teleopDuration);
                        break;
                    case MatchPhase.Teleop when remaining == TimeSpan.Zero:
                        nextPhase = TransitionToLocked(MatchPhase.PostMatch, TimeSpan.Zero);
                        break;
                }
            }

            if (nextPhase.HasValue)
            {
                PhaseChanged?.Invoke(nextPhase.Value);
                continue;
            }

            break;
        }
    }

    // ── E-Stop ─────────────────────────────────────────────────────────────────

    public void TriggerArenaEstop()
    {
        bool shouldAbort;
        lock (_stateLock)
        {
            _arenaEstop = true;
            shouldAbort = _phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition or MatchPhase.Teleop;
        }

        if (shouldAbort)
            AbortMatch();
    }

    /// <summary>Clears the arena e-stop. Only valid when match is not running.</summary>
    public void ResetArenaEstop()
    {
        lock (_stateLock)
        {
            if (_phase is MatchPhase.Auto or MatchPhase.AutoToTeleopTransition or MatchPhase.Teleop)
                throw new InvalidOperationException("Cannot reset e-stop while match is running.");

            _arenaEstop = false;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private MatchPhase TransitionToLocked(MatchPhase next, TimeSpan duration)
    {
        _phase = next;
        _phaseDuration = duration;
        _phaseTimer.Restart();
        return next;
    }
}
