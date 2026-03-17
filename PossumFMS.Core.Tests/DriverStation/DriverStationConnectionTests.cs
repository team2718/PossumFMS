using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using Xunit;

namespace PossumFMS.Core.Tests.DriverStation;

public sealed class DriverStationConnectionTests
{
    private static DriverStationConnection Make(AllianceStation? station = null)
        => new(station ?? AllianceStations.Red1);

    // ── Construction ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsStation()
    {
        var ds = Make(AllianceStations.Blue2);

        Assert.Equal(AllianceStations.Blue2, ds.Station);
    }

    [Fact]
    public void Constructor_DefaultTeamNumberIsZero()
    {
        var ds = Make();

        Assert.Equal(0, ds.TeamNumber);
    }

    [Fact]
    public void Constructor_DefaultWpaKeyIsEmpty()
    {
        var ds = Make();

        Assert.Equal(string.Empty, ds.WpaKey);
    }

    [Fact]
    public void Constructor_LinkStatusesAllFalse()
    {
        var ds = Make();

        Assert.False(ds.DsLinked);
        Assert.False(ds.RadioLinked);
        Assert.False(ds.RioLinked);
        Assert.False(ds.RobotLinked);
    }

    [Fact]
    public void Constructor_BatteryVoltageIsZero()
    {
        var ds = Make();

        Assert.Equal(0.0, ds.BatteryVoltage);
    }

    [Fact]
    public void Constructor_StopsAreFalse()
    {
        var ds = Make();

        Assert.False(ds.Estop);
        Assert.False(ds.Astop);
        Assert.False(ds.Bypassed);
    }

    [Fact]
    public void Constructor_WrongStationIsEmpty()
    {
        var ds = Make();

        Assert.Equal(string.Empty, ds.WrongStation);
    }

    // ── IsLinked ───────────────────────────────────────────────────────────────

    [Fact]
    public void IsLinked_WhenBothDsAndRobotLinked_True()
    {
        var ds = Make();
        ds.DsLinked    = true;
        ds.RobotLinked = true;

        Assert.True(ds.IsLinked);
    }

    [Fact]
    public void IsLinked_WhenOnlyDsLinked_False()
    {
        var ds = Make();
        ds.DsLinked    = true;
        ds.RobotLinked = false;

        Assert.False(ds.IsLinked);
    }

    [Fact]
    public void IsLinked_WhenOnlyRobotLinked_False()
    {
        var ds = Make();
        ds.DsLinked    = false;
        ds.RobotLinked = true;

        Assert.False(ds.IsLinked);
    }

    [Fact]
    public void IsLinked_WhenNeitherLinked_False()
    {
        var ds = Make();

        Assert.False(ds.IsLinked);
    }

    // ── IsReady ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsReady_WhenLinkedAndNoEstop_True()
    {
        var ds = Make();
        ds.DsLinked    = true;
        ds.RobotLinked = true;
        ds.Estop       = false;

        Assert.True(ds.IsReady);
    }

    [Fact]
    public void IsReady_WhenBypassedAndNoEstop_True()
    {
        var ds = Make();
        ds.Bypassed = true;
        ds.Estop    = false;

        Assert.True(ds.IsReady);
    }

    [Fact]
    public void IsReady_WhenLinkedButEstopped_False()
    {
        var ds = Make();
        ds.DsLinked    = true;
        ds.RobotLinked = true;
        ds.Estop       = true;

        Assert.False(ds.IsReady);
    }

    [Fact]
    public void IsReady_WhenBypassedAndEstopped_False()
    {
        var ds = Make();
        ds.Bypassed = true;
        ds.Estop    = true;

        Assert.False(ds.IsReady);
    }

    [Fact]
    public void IsReady_WhenNotLinkedAndNotBypassed_False()
    {
        var ds = Make();

        Assert.False(ds.IsReady);
    }

    [Fact]
    public void IsReady_WhenDsLinkedButRobotNotLinked_False()
    {
        var ds = Make();
        ds.DsLinked    = true;
        ds.RobotLinked = false;
        ds.Estop       = false;

        Assert.False(ds.IsReady);
    }

    // ── Stops ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Estop_CanBeSetAndCleared()
    {
        var ds = Make();

        ds.Estop = true;
        Assert.True(ds.Estop);

        ds.Estop = false;
        Assert.False(ds.Estop);
    }

    [Fact]
    public void Astop_CanBeSetAndCleared()
    {
        var ds = Make();

        ds.Astop = true;
        Assert.True(ds.Astop);

        ds.Astop = false;
        Assert.False(ds.Astop);
    }

    [Fact]
    public void Bypassed_CanBeSetAndCleared()
    {
        var ds = Make();

        ds.Bypassed = true;
        Assert.True(ds.Bypassed);

        ds.Bypassed = false;
        Assert.False(ds.Bypassed);
    }

    // ── Station identity ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(AllianceColor.Red,  StationPosition.One)]
    [InlineData(AllianceColor.Red,  StationPosition.Two)]
    [InlineData(AllianceColor.Red,  StationPosition.Three)]
    [InlineData(AllianceColor.Blue, StationPosition.One)]
    [InlineData(AllianceColor.Blue, StationPosition.Two)]
    [InlineData(AllianceColor.Blue, StationPosition.Three)]
    public void Station_MatchesConstructorArgument(AllianceColor color, StationPosition pos)
    {
        var station = new AllianceStation(color, pos);
        var ds = new DriverStationConnection(station);

        Assert.Equal(color, ds.Station.Color);
        Assert.Equal(pos,   ds.Station.Position);
    }

    // ── Packet/connection state ────────────────────────────────────────────────

    [Fact]
    public void MissedPacketCount_DefaultIsZero()
    {
        var ds = Make();

        Assert.Equal(0, ds.MissedPacketCount);
    }

    [Fact]
    public void DsRobotTripTimeMs_DefaultIsZero()
    {
        var ds = Make();

        Assert.Equal(0, ds.DsRobotTripTimeMs);
    }

    [Fact]
    public void SecondsSinceLastRobotLink_DefaultIsZero()
    {
        var ds = Make();

        Assert.Equal(0.0, ds.SecondsSinceLastRobotLink);
    }
}
