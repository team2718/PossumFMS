using PossumFMS.Core.Arena;
using Xunit;

namespace PossumFMS.Core.Tests.Arena;

public sealed class MatchStateTests
{
    // ── AllianceScore ──────────────────────────────────────────────────────────

    [Fact]
    public void AllianceScore_DefaultAllFieldsZero()
    {
        var score = new AllianceScore();

        Assert.Equal(0, score.AutoFuelPoints);
        Assert.Equal(0, score.AutoTowerPoints);
        Assert.Equal(0, score.TeleopFuelPoints);
        Assert.Equal(0, score.TeleopTowerPoints);
        Assert.Equal(0, score.PenaltyPoints);
        Assert.Equal(0, score.Total);
    }

    [Fact]
    public void AllianceScore_Total_SumsAllFourFields()
    {
        var score = new AllianceScore
        {
            AutoFuelPoints   = 5,
            AutoTowerPoints  = 10,
            TeleopFuelPoints = 15,
            TeleopTowerPoints = 20,
            PenaltyPoints = 25,
        };

        Assert.Equal(75, score.Total);
    }

    [Fact]
    public void AllianceScore_Reset_ZeroesAllFields()
    {
        var score = new AllianceScore
        {
            AutoFuelPoints    = 10,
            AutoTowerPoints   = 20,
            TeleopFuelPoints  = 30,
            TeleopTowerPoints = 40,
        };

        score.Reset();

        Assert.Equal(0, score.AutoFuelPoints);
        Assert.Equal(0, score.AutoTowerPoints);
        Assert.Equal(0, score.TeleopFuelPoints);
        Assert.Equal(0, score.TeleopTowerPoints);
        Assert.Equal(0, score.PenaltyPoints);
        Assert.Equal(0, score.Total);
    }

    [Fact]
    public void AllianceScore_TotalIsZeroAfterReset()
    {
        var score = new AllianceScore { AutoFuelPoints = 100, TeleopFuelPoints = 200, PenaltyPoints = 15 };
        score.Reset();

        Assert.Equal(0, score.Total);
    }

    [Fact]
    public void ViolationRules_MinorAndMajorFoul_AwardExpectedPoints()
    {
        Assert.Equal(5, ViolationRules.GetAwardedPoints(ViolationType.MinorFoul));
        Assert.Equal(15, ViolationRules.GetAwardedPoints(ViolationType.MajorFoul));
    }

    [Fact]
    public void ViolationRules_OnlyMinorAndMajorFoul_AreImplemented()
    {
        Assert.True(ViolationRules.IsImplementedFoul(ViolationType.MinorFoul));
        Assert.True(ViolationRules.IsImplementedFoul(ViolationType.MajorFoul));
        Assert.False(ViolationRules.IsImplementedFoul(ViolationType.YellowCard));
        Assert.False(ViolationRules.IsImplementedFoul(ViolationType.RedCard));
    }

    // ── AllianceStations ───────────────────────────────────────────────────────

    [Fact]
    public void AllianceStations_All_ContainsSixElements()
    {
        Assert.Equal(6, AllianceStations.All.Count);
    }

    [Fact]
    public void AllianceStations_All_ContainsThreeRedAndThreeBlue()
    {
        var reds  = AllianceStations.All.Count(s => s.Color == AllianceColor.Red);
        var blues = AllianceStations.All.Count(s => s.Color == AllianceColor.Blue);

        Assert.Equal(3, reds);
        Assert.Equal(3, blues);
    }

    [Fact]
    public void AllianceStations_All_HasCorrectPositions()
    {
        var redPositions  = AllianceStations.All.Where(s => s.Color == AllianceColor.Red)
                                                .Select(s => s.Position).OrderBy(p => p).ToList();
        var bluePositions = AllianceStations.All.Where(s => s.Color == AllianceColor.Blue)
                                                .Select(s => s.Position).OrderBy(p => p).ToList();

        Assert.Equal(new[] { StationPosition.One, StationPosition.Two, StationPosition.Three }, redPositions);
        Assert.Equal(new[] { StationPosition.One, StationPosition.Two, StationPosition.Three }, bluePositions);
    }

    [Fact]
    public void AllianceStations_StaticProperties_MatchAllList()
    {
        Assert.Contains(AllianceStations.Red1,  AllianceStations.All);
        Assert.Contains(AllianceStations.Red2,  AllianceStations.All);
        Assert.Contains(AllianceStations.Red3,  AllianceStations.All);
        Assert.Contains(AllianceStations.Blue1, AllianceStations.All);
        Assert.Contains(AllianceStations.Blue2, AllianceStations.All);
        Assert.Contains(AllianceStations.Blue3, AllianceStations.All);
    }

    [Fact]
    public void AllianceStations_Red1_IsRedPositionOne()
    {
        Assert.Equal(AllianceColor.Red,      AllianceStations.Red1.Color);
        Assert.Equal(StationPosition.One,    AllianceStations.Red1.Position);
    }

    [Fact]
    public void AllianceStations_Blue3_IsBluePositionThree()
    {
        Assert.Equal(AllianceColor.Blue,     AllianceStations.Blue3.Color);
        Assert.Equal(StationPosition.Three,  AllianceStations.Blue3.Position);
    }

    [Fact]
    public void AllianceStations_AllUnique()
    {
        var distinct = AllianceStations.All.Distinct().Count();
        Assert.Equal(AllianceStations.All.Count, distinct);
    }

    // ── AllianceStation record equality ───────────────────────────────────────

    [Fact]
    public void AllianceStation_SameColorAndPosition_AreEqual()
    {
        var a = new AllianceStation(AllianceColor.Red, StationPosition.Two);
        var b = new AllianceStation(AllianceColor.Red, StationPosition.Two);

        Assert.Equal(a, b);
    }

    [Fact]
    public void AllianceStation_DifferentColor_AreNotEqual()
    {
        var a = new AllianceStation(AllianceColor.Red,  StationPosition.One);
        var b = new AllianceStation(AllianceColor.Blue, StationPosition.One);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AllianceStation_DifferentPosition_AreNotEqual()
    {
        var a = new AllianceStation(AllianceColor.Red, StationPosition.One);
        var b = new AllianceStation(AllianceColor.Red, StationPosition.Two);

        Assert.NotEqual(a, b);
    }

    // ── MatchType enum values ──────────────────────────────────────────────────

    [Fact]
    public void MatchType_None_IsZero()
    {
        Assert.Equal(0, (byte)PossumFMS.Core.Arena.MatchType.Test);
    }

    [Fact]
    public void MatchType_Practice_IsOne()
    {
        Assert.Equal(1, (byte)PossumFMS.Core.Arena.MatchType.Practice);
    }

    [Fact]
    public void MatchType_Qualification_IsTwo()
    {
        Assert.Equal(2, (byte)PossumFMS.Core.Arena.MatchType.Qualification);
    }

    [Fact]
    public void MatchType_Playoff_IsThree()
    {
        Assert.Equal(3, (byte)PossumFMS.Core.Arena.MatchType.Playoff);
    }

    // ── TowerEndgameLevel enum values ──────────────────────────────────────────

    [Fact]
    public void TowerEndgameLevel_None_IsZero()
    {
        Assert.Equal(0, (int)TowerEndgameLevel.None);
    }

    [Fact]
    public void TowerEndgameLevel_L1_IsOne()
    {
        Assert.Equal(1, (int)TowerEndgameLevel.L1);
    }

    [Fact]
    public void TowerEndgameLevel_L2_IsTwo()
    {
        Assert.Equal(2, (int)TowerEndgameLevel.L2);
    }

    [Fact]
    public void TowerEndgameLevel_L3_IsThree()
    {
        Assert.Equal(3, (int)TowerEndgameLevel.L3);
    }

    // ── StationPosition enum values ────────────────────────────────────────────

    [Fact]
    public void StationPosition_One_IsOne()
    {
        Assert.Equal(1, (int)StationPosition.One);
    }

    [Fact]
    public void StationPosition_Two_IsTwo()
    {
        Assert.Equal(2, (int)StationPosition.Two);
    }

    [Fact]
    public void StationPosition_Three_IsThree()
    {
        Assert.Equal(3, (int)StationPosition.Three);
    }
}
