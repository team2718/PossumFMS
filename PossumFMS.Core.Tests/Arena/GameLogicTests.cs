using PossumFMS.Core.Arena;
using Xunit;

namespace PossumFMS.Core.Tests.Arena;

public sealed class GameLogicTests
{
    private static (PossumFMS.Core.Arena.Arena Arena, GameLogic Logic) Create()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        var logic = new GameLogic(arena);
        return (arena, logic);
    }

    // ── Initial state ──────────────────────────────────────────────────────────

    [Fact]
    public void InitialScores_AreZero()
    {
        var (_, logic) = Create();

        Assert.Equal(0, logic.RedScore.Total);
        Assert.Equal(0, logic.BlueScore.Total);
    }

    [Fact]
    public void ShiftAutoWinnerAlliance_InitiallyNull()
    {
        var (_, logic) = Create();

        Assert.Null(logic.ShiftAutoWinnerAlliance);
    }

    [Fact]
    public void GetAutoTowerClimbed_DefaultFalseForAllStations()
    {
        var (_, logic) = Create();

        foreach (var station in AllianceStations.All)
            Assert.False(logic.GetAutoTowerClimbed(station));
    }

    [Fact]
    public void GetEndgameTowerLevel_DefaultNoneForAllStations()
    {
        var (_, logic) = Create();

        foreach (var station in AllianceStations.All)
            Assert.Equal(TowerEndgameLevel.None, logic.GetEndgameTowerLevel(station));
    }

    // ── Hub active / inactive (non-Teleop phases) ──────────────────────────────

    [Fact]
    public void IsHubActive_WhenIdle_ReturnsTrue()
    {
        var (_, logic) = Create();

        Assert.True(logic.IsHubActive(AllianceColor.Red));
        Assert.True(logic.IsHubActive(AllianceColor.Blue));
    }

    [Fact]
    public void IsHubActive_WhenPreMatch_ReturnsFalse()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();

        Assert.False(logic.IsHubActive(AllianceColor.Red));
        Assert.False(logic.IsHubActive(AllianceColor.Blue));
    }

    [Fact]
    public void IsHubActive_WhenAuto_ReturnsTrue()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        Assert.True(logic.IsHubActive(AllianceColor.Red));
        Assert.True(logic.IsHubActive(AllianceColor.Blue));
    }

    [Fact]
    public void IsHubStrictlyActive_WhenIdle_ReturnsFalse()
    {
        var (_, logic) = Create();

        Assert.False(logic.IsHubStrictlyActive(AllianceColor.Red));
        Assert.False(logic.IsHubStrictlyActive(AllianceColor.Blue));
    }

    [Fact]
    public void IsHubStrictlyActive_WhenAuto_ReturnsTrue()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        Assert.True(logic.IsHubStrictlyActive(AllianceColor.Red));
        Assert.True(logic.IsHubStrictlyActive(AllianceColor.Blue));
    }

    [Fact]
    public void IsHubAboutToBecomeInactive_WhenNotTeleop_ReturnsFalse()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto

        Assert.False(logic.IsHubAboutToBecomeInactive(AllianceColor.Red, TimeSpan.FromSeconds(10)));
        Assert.False(logic.IsHubAboutToBecomeInactive(AllianceColor.Blue, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void IsHubAboutToBecomeInactive_NegativeWindow_ReturnsFalse()
    {
        var (_, logic) = Create();

        // Even if somehow in teleop, negative within should be false
        Assert.False(logic.IsHubAboutToBecomeInactive(AllianceColor.Red, TimeSpan.FromSeconds(-1)));
    }

    // ── CurrentTeleopPeriod ────────────────────────────────────────────────────

    [Fact]
    public void CurrentTeleopPeriod_WhenNotTeleop_IsNotStarted()
    {
        var (arena, logic) = Create();

        Assert.Equal(TeleopPeriod.NotStarted, logic.CurrentTeleopPeriod);

        arena.StartPreMatch();
        Assert.Equal(TeleopPeriod.NotStarted, logic.CurrentTeleopPeriod);

        arena.StartMatch(); // Auto
        Assert.Equal(TeleopPeriod.NotStarted, logic.CurrentTeleopPeriod);
    }

    // ── ScoreFuel ──────────────────────────────────────────────────────────────

    [Fact]
    public void ScoreFuel_InAuto_AddsToAutoFuelPoints()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        logic.ScoreFuel(AllianceColor.Red, 5);

        Assert.Equal(5, logic.RedScore.AutoFuelPoints);
        Assert.Equal(0, logic.RedScore.TeleopFuelPoints);
    }

    [Fact]
    public void ScoreFuel_BlueInAuto_DoesNotAffectRed()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        logic.ScoreFuel(AllianceColor.Blue, 3);

        Assert.Equal(0, logic.RedScore.AutoFuelPoints);
        Assert.Equal(3, logic.BlueScore.AutoFuelPoints);
    }

    [Fact]
    public void ScoreFuel_ZeroCount_IsNoOp()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        logic.ScoreFuel(AllianceColor.Red, 0);

        Assert.Equal(0, logic.RedScore.AutoFuelPoints);
    }

    [Fact]
    public void ScoreFuel_NegativeCount_IsNoOp()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        logic.ScoreFuel(AllianceColor.Red, -5);

        Assert.Equal(0, logic.RedScore.AutoFuelPoints);
    }

    [Fact]
    public void ScoreFuel_WhenIdle_AddsToTeleopFuelPoints()
    {
        var (_, logic) = Create();

        logic.ScoreFuel(AllianceColor.Red, 10);

        Assert.Equal(0, logic.RedScore.AutoFuelPoints);
        Assert.Equal(10, logic.RedScore.TeleopFuelPoints);
    }

    [Fact]
    public void ScoreFuel_WhenPreMatch_IsNoOp()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();

        logic.ScoreFuel(AllianceColor.Red, 10);

        Assert.Equal(0, logic.RedScore.AutoFuelPoints);
        Assert.Equal(0, logic.RedScore.TeleopFuelPoints);
    }

    [Fact]
    public void ScoreFuel_WhenPostMatch_IsNoOp()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();
        arena.AbortMatch();

        logic.ScoreFuel(AllianceColor.Red, 10);

        Assert.Equal(0, logic.RedScore.AutoFuelPoints);
        Assert.Equal(0, logic.RedScore.TeleopFuelPoints);
    }

    // ── AdjustFuelPoints ───────────────────────────────────────────────────────

    [Fact]
    public void AdjustFuelPoints_PositiveDelta_Increases()
    {
        var (_, logic) = Create();

        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: 10);

        Assert.Equal(10, logic.RedScore.AutoFuelPoints);
    }

    [Fact]
    public void AdjustFuelPoints_NegativeDelta_DecreasesButNotBelowZero()
    {
        var (_, logic) = Create();

        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: 5);
        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: -10); // attempt to go below 0

        Assert.Equal(0, logic.RedScore.AutoFuelPoints);
    }

    [Fact]
    public void AdjustFuelPoints_IsAuto_False_AffectsTeleop()
    {
        var (_, logic) = Create();

        logic.AdjustFuelPoints(AllianceColor.Blue, isAuto: false, delta: 7);

        Assert.Equal(7, logic.BlueScore.TeleopFuelPoints);
        Assert.Equal(0, logic.BlueScore.AutoFuelPoints);
    }

    [Fact]
    public void AddViolation_MinorFoul_AwardsPointsToOpposingAlliance()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        var violation = logic.AddViolation(AllianceStations.Red2, 254, "G415");

        Assert.Equal(5, logic.BlueScore.PenaltyPoints);
        Assert.Equal(0, logic.RedScore.PenaltyPoints);
        Assert.Single(logic.Violations);
        Assert.Equal(254, violation.TeamNumber);
        Assert.Equal(MatchPhase.Auto, violation.Phase);
        Assert.InRange(violation.TimeRemainingSeconds, 19.0, 20.0);
    }

    [Fact]
    public void AddViolation_MajorFoul_AffectsTotalScore()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: 10);
        logic.AddViolation(AllianceStations.Red1, 111, "G420");

        Assert.Equal(10, logic.RedScore.Total);
        Assert.Equal(15, logic.BlueScore.Total);
    }

    [Fact]
    public void RemoveViolation_RemovesPenaltyPoints()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        var violation = logic.AddViolation(AllianceStations.Blue3, 999, "G420");

        Assert.True(logic.RemoveViolation(violation.Id));
        Assert.Empty(logic.Violations);
        Assert.Equal(0, logic.RedScore.PenaltyPoints);
        Assert.Equal(0, logic.BlueScore.PenaltyPoints);
    }

    [Fact]
    public void RemoveViolation_UnknownId_ReturnsFalse()
    {
        var (_, logic) = Create();

        Assert.False(logic.RemoveViolation(Guid.NewGuid()));
    }

    [Fact]
    public void Violations_ResetWhenArenaReturnsToIdle()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();

        logic.AddViolation(AllianceStations.Red3, 604, "G415");
        arena.AbortMatch();
        arena.ClearMatch();

        Assert.Empty(logic.Violations);
        Assert.Equal(0, logic.RedScore.PenaltyPoints);
        Assert.Equal(0, logic.BlueScore.PenaltyPoints);
    }

    [Fact]
    public void AdjustFuelPoints_ZeroDelta_IsNoOp()
    {
        var (_, logic) = Create();
        bool fired = false;
        logic.ScoreChanged += () => fired = true;

        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: 0);

        Assert.False(fired);
    }

    [Fact]
    public void AdjustFuelPoints_FiresScoreChanged()
    {
        var (_, logic) = Create();
        bool fired = false;
        logic.ScoreChanged += () => fired = true;

        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: 5);

        Assert.True(fired);
    }

    // ── SetAutoTowerClimbed ────────────────────────────────────────────────────

    [Fact]
    public void SetAutoTowerClimbed_Once_Adds15Points()
    {
        var (_, logic) = Create();

        logic.SetAutoTowerClimbed(AllianceStations.Red1, true);

        Assert.Equal(15, logic.RedScore.AutoTowerPoints);
    }

    [Fact]
    public void SetAutoTowerClimbed_ThreeRobots_Adds45Points()
    {
        var (_, logic) = Create();

        logic.SetAutoTowerClimbed(AllianceStations.Red1, true);
        logic.SetAutoTowerClimbed(AllianceStations.Red2, true);
        logic.SetAutoTowerClimbed(AllianceStations.Red3, true);

        Assert.Equal(45, logic.RedScore.AutoTowerPoints);
    }

    [Fact]
    public void SetAutoTowerClimbed_DoesNotAffectOppositeAlliance()
    {
        var (_, logic) = Create();

        logic.SetAutoTowerClimbed(AllianceStations.Red1, true);

        Assert.Equal(0, logic.BlueScore.AutoTowerPoints);
    }

    [Fact]
    public void SetAutoTowerClimbed_SettingSameValueTwice_DoesNotFire()
    {
        var (_, logic) = Create();
        logic.SetAutoTowerClimbed(AllianceStations.Red1, true);
        int count = 0;
        logic.ScoreChanged += () => count++;

        logic.SetAutoTowerClimbed(AllianceStations.Red1, true); // Same value

        Assert.Equal(0, count);
    }

    [Fact]
    public void SetAutoTowerClimbed_False_RemovesPoints()
    {
        var (_, logic) = Create();
        logic.SetAutoTowerClimbed(AllianceStations.Blue2, true);

        logic.SetAutoTowerClimbed(AllianceStations.Blue2, false);

        Assert.Equal(0, logic.BlueScore.AutoTowerPoints);
    }

    [Fact]
    public void GetAutoTowerClimbed_ReflectsSet()
    {
        var (_, logic) = Create();

        logic.SetAutoTowerClimbed(AllianceStations.Red1, true);

        Assert.True(logic.GetAutoTowerClimbed(AllianceStations.Red1));
        Assert.False(logic.GetAutoTowerClimbed(AllianceStations.Red2));
    }

    // ── SetEndgameTowerLevel ───────────────────────────────────────────────────

    [Theory]
    [InlineData(TowerEndgameLevel.L1, 10)]
    [InlineData(TowerEndgameLevel.L2, 20)]
    [InlineData(TowerEndgameLevel.L3, 30)]
    [InlineData(TowerEndgameLevel.None, 0)]
    public void SetEndgameTowerLevel_SingleRobot_CorrectPoints(TowerEndgameLevel level, int expected)
    {
        var (_, logic) = Create();

        logic.SetEndgameTowerLevel(AllianceStations.Blue1, level);

        Assert.Equal(expected, logic.BlueScore.TeleopTowerPoints);
    }

    [Fact]
    public void SetEndgameTowerLevel_ThreeRobotsAllL3_Adds90Points()
    {
        var (_, logic) = Create();

        logic.SetEndgameTowerLevel(AllianceStations.Red1, TowerEndgameLevel.L3);
        logic.SetEndgameTowerLevel(AllianceStations.Red2, TowerEndgameLevel.L3);
        logic.SetEndgameTowerLevel(AllianceStations.Red3, TowerEndgameLevel.L3);

        Assert.Equal(90, logic.RedScore.TeleopTowerPoints);
    }

    [Fact]
    public void SetEndgameTowerLevel_MixedLevels_SumsCorrectly()
    {
        var (_, logic) = Create();

        logic.SetEndgameTowerLevel(AllianceStations.Red1, TowerEndgameLevel.L1); // 10
        logic.SetEndgameTowerLevel(AllianceStations.Red2, TowerEndgameLevel.L2); // 20
        logic.SetEndgameTowerLevel(AllianceStations.Red3, TowerEndgameLevel.L3); // 30

        Assert.Equal(60, logic.RedScore.TeleopTowerPoints);
    }

    [Fact]
    public void SetEndgameTowerLevel_DoesNotAffectOppositeAlliance()
    {
        var (_, logic) = Create();

        logic.SetEndgameTowerLevel(AllianceStations.Red1, TowerEndgameLevel.L3);

        Assert.Equal(0, logic.BlueScore.TeleopTowerPoints);
    }

    [Fact]
    public void GetEndgameTowerLevel_ReflectsSet()
    {
        var (_, logic) = Create();

        logic.SetEndgameTowerLevel(AllianceStations.Blue3, TowerEndgameLevel.L2);

        Assert.Equal(TowerEndgameLevel.L2, logic.GetEndgameTowerLevel(AllianceStations.Blue3));
    }

    // ── AllianceScore.Total ────────────────────────────────────────────────────

    [Fact]
    public void Total_SumsAllFourComponents()
    {
        var (_, logic) = Create();

        // Adjust directly without phase checks
        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: 10);
        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: false, delta: 5);
        logic.SetAutoTowerClimbed(AllianceStations.Red1, true); // 15
        logic.SetEndgameTowerLevel(AllianceStations.Red2, TowerEndgameLevel.L2); // 20

        Assert.Equal(50, logic.RedScore.Total);
    }

    // ── Score reset on phase change ────────────────────────────────────────────

    [Fact]
    public void ScoreReset_OnStartPreMatch()
    {
        var (arena, logic) = Create();
        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: 20);
        logic.SetAutoTowerClimbed(AllianceStations.Blue1, true);

        arena.StartPreMatch();

        Assert.Equal(0, logic.RedScore.Total);
        Assert.Equal(0, logic.BlueScore.Total);
        Assert.False(logic.GetAutoTowerClimbed(AllianceStations.Blue1));
    }

    [Fact]
    public void ScoreReset_OnClearMatch_ViaClearedIdle()
    {
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch();
        logic.AdjustFuelPoints(AllianceColor.Blue, isAuto: true, delta: 8);
        arena.AbortMatch();

        arena.ClearMatch(); // triggers Idle phase -> reset

        Assert.Equal(0, logic.BlueScore.AutoFuelPoints);
    }

    [Fact]
    public void ScoreReset_ClearsEndgameLevelsToNone()
    {
        var (arena, logic) = Create();
        logic.SetEndgameTowerLevel(AllianceStations.Red3, TowerEndgameLevel.L3);

        arena.StartPreMatch(); // triggers PreMatch phase -> reset

        Assert.Equal(TowerEndgameLevel.None, logic.GetEndgameTowerLevel(AllianceStations.Red3));
    }

    // ── ScoreChanged event ─────────────────────────────────────────────────────

    [Fact]
    public void ScoreChanged_FiredOnSetAutoTowerClimbed()
    {
        var (_, logic) = Create();
        bool fired = false;
        logic.ScoreChanged += () => fired = true;

        logic.SetAutoTowerClimbed(AllianceStations.Red1, true);

        Assert.True(fired);
    }

    [Fact]
    public void ScoreChanged_FiredOnSetEndgameTowerLevel()
    {
        var (_, logic) = Create();
        bool fired = false;
        logic.ScoreChanged += () => fired = true;

        logic.SetEndgameTowerLevel(AllianceStations.Blue1, TowerEndgameLevel.L1);

        Assert.True(fired);
    }

    [Fact]
    public void ScoreChanged_FiredOnPreMatch_Reset()
    {
        var (arena, logic) = Create();
        bool fired = false;
        logic.ScoreChanged += () => fired = true;

        arena.StartPreMatch();

        Assert.True(fired);
    }

    // ── ShiftAutoWinnerAlliance after Teleop ───────────────────────────────────

    [Fact]
    public void AutoFuelScores_IndependentlyTracked_ReadyForShiftWinnerDetermination()
    {
        // ShiftAutoWinnerAlliance is determined when the Teleop phase begins (cannot be
        // triggered in a unit test without real time passing). This test verifies that
        // the auto fuel scores that feed into that determination are accumulated correctly
        // and independently for each alliance.
        var (arena, logic) = Create();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto

        logic.AdjustFuelPoints(AllianceColor.Red, isAuto: true, delta: 10);
        logic.AdjustFuelPoints(AllianceColor.Blue, isAuto: true, delta: 5);

        Assert.Equal(10, logic.RedScore.AutoFuelPoints);
        Assert.Equal(5, logic.BlueScore.AutoFuelPoints);
    }
}
