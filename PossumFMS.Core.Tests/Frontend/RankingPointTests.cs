using System.Text.Json;
using PossumFMS.Core.Frontend;
using Xunit;

namespace PossumFMS.Core.Tests.Frontend;

/// <summary>
/// Tests for <see cref="MatchStateBroadcaster.BuildRankingPointBreakdown"/>.
///
/// Thresholds:
///   energized    — fuelCombined  &gt;= 100
///   supercharged — fuelCombined  &gt;= 360
///   traversal    — towerCombined &gt;= 50
///   winTie       — win=3, tie=1, loss=0
///   total        — sum of all boolean RP flags + winTie
/// </summary>
public sealed class RankingPointTests
{
    /// <summary>
    /// Calls BuildRankingPointBreakdown and returns a JsonElement for assertion.
    /// Using JSON serialization avoids cross-assembly anonymous-type visibility issues.
    /// </summary>
    private static JsonElement Call(int fuelCombined, int towerCombined, bool winsMatch, bool tiedMatch)
    {
        var result = MatchStateBroadcaster.BuildRankingPointBreakdown(fuelCombined, towerCombined, winsMatch, tiedMatch);
        var json = JsonSerializer.Serialize(result);
        return JsonDocument.Parse(json).RootElement;
    }

    // ── energized (fuel >= 100) ────────────────────────────────────────────────

    [Fact]
    public void Energized_False_WhenFuel99()
    {
        var el = Call(fuelCombined: 99, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.False(el.GetProperty("energized").GetBoolean());
    }

    [Fact]
    public void Energized_True_WhenFuel100()
    {
        var el = Call(fuelCombined: 100, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.True(el.GetProperty("energized").GetBoolean());
    }

    [Fact]
    public void Energized_True_WhenFuelAbove100()
    {
        var el = Call(fuelCombined: 200, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.True(el.GetProperty("energized").GetBoolean());
    }

    // ── supercharged (fuel >= 360) ─────────────────────────────────────────────

    [Fact]
    public void Supercharged_False_WhenFuel359()
    {
        var el = Call(fuelCombined: 359, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.False(el.GetProperty("supercharged").GetBoolean());
    }

    [Fact]
    public void Supercharged_True_WhenFuel360()
    {
        var el = Call(fuelCombined: 360, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.True(el.GetProperty("supercharged").GetBoolean());
    }

    [Fact]
    public void Supercharged_False_WhenFuel100_BelowThreshold()
    {
        // energized but NOT supercharged
        var el = Call(fuelCombined: 100, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.False(el.GetProperty("supercharged").GetBoolean());
    }

    // ── traversal (tower >= 50) ────────────────────────────────────────────────

    [Fact]
    public void Traversal_False_WhenTower49()
    {
        var el = Call(fuelCombined: 0, towerCombined: 49, winsMatch: false, tiedMatch: false);
        Assert.False(el.GetProperty("traversal").GetBoolean());
    }

    [Fact]
    public void Traversal_True_WhenTower50()
    {
        var el = Call(fuelCombined: 0, towerCombined: 50, winsMatch: false, tiedMatch: false);
        Assert.True(el.GetProperty("traversal").GetBoolean());
    }

    [Fact]
    public void Traversal_True_WhenTowerAbove50()
    {
        var el = Call(fuelCombined: 0, towerCombined: 100, winsMatch: false, tiedMatch: false);
        Assert.True(el.GetProperty("traversal").GetBoolean());
    }

    // ── winTie ─────────────────────────────────────────────────────────────────

    [Fact]
    public void WinTie_Is3_WhenWin()
    {
        var el = Call(fuelCombined: 0, towerCombined: 0, winsMatch: true, tiedMatch: false);
        Assert.Equal(3, el.GetProperty("winTie").GetInt32());
    }

    [Fact]
    public void WinTie_Is1_WhenTie()
    {
        var el = Call(fuelCombined: 0, towerCombined: 0, winsMatch: false, tiedMatch: true);
        Assert.Equal(1, el.GetProperty("winTie").GetInt32());
    }

    [Fact]
    public void WinTie_Is0_WhenLoss()
    {
        var el = Call(fuelCombined: 0, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.Equal(0, el.GetProperty("winTie").GetInt32());
    }

    [Fact]
    public void WinTie_Is3_WhenBothWinAndTieAreTrue_WinTakesPriority()
    {
        // winsMatch=true takes priority over tiedMatch=true
        var el = Call(fuelCombined: 0, towerCombined: 0, winsMatch: true, tiedMatch: true);
        Assert.Equal(3, el.GetProperty("winTie").GetInt32());
    }

    // ── total ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Total_Is0_WhenAllFalse_AndLoss()
    {
        var el = Call(fuelCombined: 0, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.Equal(0, el.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Total_Is1_WhenOnlyEnergized()
    {
        var el = Call(fuelCombined: 100, towerCombined: 0, winsMatch: false, tiedMatch: false);
        Assert.Equal(1, el.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Total_Is3_WhenOnlyWin()
    {
        var el = Call(fuelCombined: 0, towerCombined: 0, winsMatch: true, tiedMatch: false);
        Assert.Equal(3, el.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Total_Is6_WhenAllRpsAndWin()
    {
        // energized(1) + supercharged(1) + traversal(1) + win(3) = 6
        var el = Call(fuelCombined: 360, towerCombined: 50, winsMatch: true, tiedMatch: false);
        Assert.Equal(6, el.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Total_Is4_WhenAllRpsAndTie()
    {
        // energized(1) + supercharged(1) + traversal(1) + tie(1) = 4
        var el = Call(fuelCombined: 360, towerCombined: 50, winsMatch: false, tiedMatch: true);
        Assert.Equal(4, el.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Total_Is3_WhenAllRpsAndLoss()
    {
        // energized(1) + supercharged(1) + traversal(1) + loss(0) = 3
        var el = Call(fuelCombined: 360, towerCombined: 50, winsMatch: false, tiedMatch: false);
        Assert.Equal(3, el.GetProperty("total").GetInt32());
    }
}
