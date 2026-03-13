namespace PossumFMS.Core.Arena;

/// <summary>
/// Owns per-match scoring state and applies game-specific rules at phase transitions.
/// </summary>
public sealed class GameLogic
{
    private readonly Arena _arena;

    public AllianceScore RedScore  { get; } = new();
    public AllianceScore BlueScore { get; } = new();

    /// <summary>Raised whenever score changes.</summary>
    public event Action? ScoreChanged;

    public GameLogic(Arena arena)
    {
        _arena = arena;
        _arena.PhaseChanged += OnPhaseChanged;
    }

    // ── Phase transitions ──────────────────────────────────────────────────────

    private void OnPhaseChanged(MatchPhase phase)
    {
        switch (phase)
        {
            case MatchPhase.PreMatch:
                // New match started — clear scores and any leftover game data.
                RedScore.Reset();
                BlueScore.Reset();
                _arena.SetGameData(string.Empty);
                ScoreChanged?.Invoke();
                break;
            case MatchPhase.Auto:
                break;
            case MatchPhase.AutoToTeleopTransition:
                break;
            case MatchPhase.Teleop:
                // Auto period just ended. Set game data to the winning alliance.
                string autoWinnerGameDataChar = RedScore.AutoFuelPoints > BlueScore.AutoFuelPoints ? "R"
                            : BlueScore.AutoFuelPoints > RedScore.AutoFuelPoints  ? "B"
                            : (new Random().Next(2) == 0 ? "R" : "B");
                _arena.SetGameData(autoWinnerGameDataChar);
                break;
            case MatchPhase.PostMatch:
                break;
        }
    }

    // ── Scoring API ─────────────────────

    // TODO: add public scoring methods here, e.g.:
    //   public void AddRedAutoPoints(int points) { RedScore.AutoPoints += points; ScoreChanged?.Invoke(); }
}
