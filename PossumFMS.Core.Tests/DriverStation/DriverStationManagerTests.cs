using Microsoft.Extensions.Logging.Abstractions;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using Xunit;

namespace PossumFMS.Core.Tests.DriverStation;

public sealed class DriverStationManagerTests
{
    private static DriverStationManager CreateManager(PossumFMS.Core.Arena.Arena? arena = null)
    {
        arena ??= new PossumFMS.Core.Arena.Arena();
        return new DriverStationManager(arena, NullLogger<DriverStationManager>.Instance);
    }

    // ── Constructor / Stations ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_CreatesAllSixStations()
    {
        var mgr = CreateManager();

        Assert.Equal(6, mgr.Stations.Count);
    }

    [Fact]
    public void Constructor_ContainsAllKnownStations()
    {
        var mgr = CreateManager();

        foreach (var station in AllianceStations.All)
            Assert.True(mgr.Stations.ContainsKey(station));
    }

    [Fact]
    public void Indexer_ReturnsSameObjectAsStations()
    {
        var mgr = CreateManager();

        var viaIndex  = mgr[AllianceStations.Red1];
        var viaDict   = mgr.Stations[AllianceStations.Red1];

        Assert.Same(viaIndex, viaDict);
    }

    // ── AssignTeam ─────────────────────────────────────────────────────────────

    [Fact]
    public void AssignTeam_SetsTeamNumber()
    {
        var mgr = CreateManager();

        mgr.AssignTeam(AllianceStations.Red1, 1234);

        Assert.Equal(1234, mgr[AllianceStations.Red1].TeamNumber);
    }

    [Fact]
    public void AssignTeam_SetsWpaKey()
    {
        var mgr = CreateManager();

        mgr.AssignTeam(AllianceStations.Blue3, 5678, "mypassword");

        Assert.Equal("mypassword", mgr[AllianceStations.Blue3].WpaKey);
    }

    [Fact]
    public void AssignTeam_ZeroTeamNumber_ClearsWpaKey()
    {
        var mgr = CreateManager();
        mgr.AssignTeam(AllianceStations.Red2, 9999, "secret");

        mgr.AssignTeam(AllianceStations.Red2, 0);

        Assert.Equal(string.Empty, mgr[AllianceStations.Red2].WpaKey);
        Assert.Equal(0, mgr[AllianceStations.Red2].TeamNumber);
    }

    [Fact]
    public void AssignTeam_NegativeTeamNumber_Throws()
    {
        var mgr = CreateManager();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => mgr.AssignTeam(AllianceStations.Red1, -1));
    }

    [Fact]
    public void AssignTeam_FiresTeamAssignmentsChangedEvent()
    {
        var mgr = CreateManager();
        bool fired = false;
        mgr.TeamAssignmentsChanged += () => fired = true;

        mgr.AssignTeam(AllianceStations.Red1, 9999);

        Assert.True(fired);
    }

    [Fact]
    public void AssignTeam_ZeroWithoutKey_KeyIsEmpty()
    {
        var mgr = CreateManager();

        mgr.AssignTeam(AllianceStations.Blue1, 0);

        Assert.Equal(string.Empty, mgr[AllianceStations.Blue1].WpaKey);
    }

    [Fact]
    public void AssignTeam_DifferentStations_AreIndependent()
    {
        var mgr = CreateManager();

        mgr.AssignTeam(AllianceStations.Red1, 100);
        mgr.AssignTeam(AllianceStations.Red2, 200);

        Assert.Equal(100, mgr[AllianceStations.Red1].TeamNumber);
        Assert.Equal(200, mgr[AllianceStations.Red2].TeamNumber);
    }

    // ── Estop ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Estop_SetsEstopOnStation()
    {
        var mgr = CreateManager();

        mgr.Estop(AllianceStations.Red1);

        Assert.True(mgr[AllianceStations.Red1].Estop);
    }

    [Fact]
    public void Estop_DoesNotAffectOtherStations()
    {
        var mgr = CreateManager();

        mgr.Estop(AllianceStations.Red1);

        Assert.False(mgr[AllianceStations.Red2].Estop);
        Assert.False(mgr[AllianceStations.Blue1].Estop);
    }

    // ── Astop ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Astop_WhenAuto_SetsAstopOnStation()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto phase
        var mgr = CreateManager(arena);

        mgr.Astop(AllianceStations.Blue2);

        Assert.True(mgr[AllianceStations.Blue2].Astop);
    }

    [Fact]
    public void Astop_WhenNotAuto_DoesNothing()
    {
        var arena = new PossumFMS.Core.Arena.Arena(); // Idle
        var mgr = CreateManager(arena);

        mgr.Astop(AllianceStations.Blue2);

        Assert.False(mgr[AllianceStations.Blue2].Astop);
    }

    [Fact]
    public void AstopAll_WhenAuto_SetsAstopOnAllStations()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();
        var mgr = CreateManager(arena);

        mgr.AstopAll();

        foreach (var ds in mgr.Stations.Values)
            Assert.True(ds.Astop);
    }

    [Fact]
    public void AstopAll_WhenNotAuto_DoesNothing()
    {
        var arena = new PossumFMS.Core.Arena.Arena(); // Idle
        var mgr = CreateManager(arena);

        mgr.AstopAll();

        foreach (var ds in mgr.Stations.Values)
            Assert.False(ds.Astop);
    }

    // ── ResetStops ─────────────────────────────────────────────────────────────

    [Fact]
    public void ResetStops_ClearsEstopAndAstop()
    {
        var mgr = CreateManager();
        mgr[AllianceStations.Red3].Estop = true;
        mgr[AllianceStations.Red3].Astop = true;

        mgr.ResetStops(AllianceStations.Red3);

        Assert.False(mgr[AllianceStations.Red3].Estop);
        Assert.False(mgr[AllianceStations.Red3].Astop);
    }

    [Fact]
    public void ResetStops_DoesNotAffectOtherStations()
    {
        var mgr = CreateManager();
        mgr[AllianceStations.Red1].Estop = true;
        mgr[AllianceStations.Red2].Estop = true;

        mgr.ResetStops(AllianceStations.Red1);

        Assert.True(mgr[AllianceStations.Red2].Estop);
    }

    // ── ResetAllStops ──────────────────────────────────────────────────────────

    [Fact]
    public void ResetAllStops_ClearsEstopOnAllStations()
    {
        var mgr = CreateManager();
        foreach (var ds in mgr.Stations.Values)
            ds.Estop = true;

        mgr.ResetAllStops();

        foreach (var ds in mgr.Stations.Values)
            Assert.False(ds.Estop);
    }

    [Fact]
    public void ResetAllStops_ClearsAstopOnAllStations()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto phase
        var mgr = CreateManager(arena);
        mgr.AstopAll();

        mgr.ResetAllStops();

        foreach (var ds in mgr.Stations.Values)
            Assert.False(ds.Astop);
    }

    // ── SetBypass ──────────────────────────────────────────────────────────────

    [Fact]
    public void SetBypass_True_SetsBypassedOnStation()
    {
        var mgr = CreateManager();

        mgr.SetBypass(AllianceStations.Blue1, true);

        Assert.True(mgr[AllianceStations.Blue1].Bypassed);
    }

    [Fact]
    public void SetBypass_False_ClearsBypassedOnStation()
    {
        var mgr = CreateManager();
        mgr.SetBypass(AllianceStations.Blue1, true);

        mgr.SetBypass(AllianceStations.Blue1, false);

        Assert.False(mgr[AllianceStations.Blue1].Bypassed);
    }

    [Fact]
    public void SetBypass_DoesNotAffectOtherStations()
    {
        var mgr = CreateManager();

        mgr.SetBypass(AllianceStations.Blue1, true);

        Assert.False(mgr[AllianceStations.Blue2].Bypassed);
    }

    // ── GetLoopTimingSnapshot ──────────────────────────────────────────────────

    [Fact]
    public void GetLoopTimingSnapshot_ReturnsZerosWhenLoopHasNotRun()
    {
        var mgr = CreateManager();

        var (current, max) = mgr.GetLoopTimingSnapshot();

        Assert.Equal(0.0, current);
        Assert.Equal(0.0, max);
    }

    // ── Astop auto-clear on Teleop ─────────────────────────────────────────────

    [Fact]
    public void Astop_ClearsWhenArenaPhaseChangesToTeleop()
    {
        // OnArenaPhaseChanged is the handler wired in ExecuteAsync.
        // We call it directly (internal visibility) to test the auto-clear logic
        // without needing to advance real time to reach the Teleop phase.
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto
        var mgr = CreateManager(arena);
        mgr.AstopAll();

        foreach (var ds in mgr.Stations.Values)
            Assert.True(ds.Astop); // confirm astops are set before the transition

        mgr.OnArenaPhaseChanged(MatchPhase.Teleop);

        foreach (var ds in mgr.Stations.Values)
            Assert.False(ds.Astop); // handler must clear all astops when Teleop begins
    }
}
