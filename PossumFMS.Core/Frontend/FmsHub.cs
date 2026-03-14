using Microsoft.AspNetCore.SignalR;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using PossumFMS.Core.Network;

namespace PossumFMS.Core.Frontend;

/// <summary>
/// SignalR hub for the FMS frontend website. Clients subscribe to receive
/// real-time match state updates and send match control / team assignment commands.
///
/// Connect from the frontend:
///   const conn = new signalR.HubConnectionBuilder().withUrl("/fmshub").build();
/// </summary>
public sealed class FmsHub(
    Arena.Arena arena,
    DriverStationManager dsManager,
    MatchStateBroadcaster broadcaster,
    ILogger<FmsHub> logger) : Hub
{
    // ── Team assignment ────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a team to a station. Triggers automatic VH-113 reconfiguration.
    /// Pass teamNumber = 0 to clear the slot.
    /// </summary>
    public async Task AssignTeam(int stationIndex, int teamNumber, string wpaKey = "")
    {
        var station = AllianceStations.All[stationIndex];
        logger.LogInformation("Assigning team {Team} to {Station}.", teamNumber, station);
        dsManager.AssignTeam(station, teamNumber, wpaKey);
        await BroadcastMatchState();
    }

    // ── Match control ──────────────────────────────────────────────────────────

    public async Task StartPreMatch()
    {
        logger.LogInformation("StartPreMatch requested by {Client}.", Context.ConnectionId);
        arena.StartPreMatch();
        await BroadcastMatchState();
    }

    public async Task StartMatch()
    {
        logger.LogInformation("StartMatch requested by {Client}.", Context.ConnectionId);
        // Only start if every non-bypassed station has a driver station linked.
        if (!dsManager.Stations.Values.All(s => s.IsReady))
        {
            logger.LogWarning("StartMatch blocked — not all stations ready.");
            return;
        }
        arena.StartMatch();
        await BroadcastMatchState();
    }

    public async Task AbortMatch()
    {
        logger.LogInformation("AbortMatch requested by {Client}.", Context.ConnectionId);
        arena.AbortMatch();
        await BroadcastMatchState();
    }

    public async Task ClearMatch()
    {
        logger.LogInformation("ClearMatch requested by {Client}.", Context.ConnectionId);
        dsManager.ResetAllStops();
        arena.ResetArenaEstop();
        arena.ClearMatch();
        await BroadcastMatchState();
    }

    public async Task TriggerArenaEstop()
    {
        logger.LogWarning("ArenaEstop triggered by {Client}.", Context.ConnectionId);
        arena.TriggerArenaEstop();
        await BroadcastMatchState();
    }

    public async Task ResetArenaEstop()
    {
        arena.ResetArenaEstop();
        await BroadcastMatchState();
    }

    // Station-level stops (stationIndex: 0=Red1 … 5=Blue3)
    public async Task EstopStation(int stationIndex)
    {
        var station = AllianceStations.All[stationIndex];
        logger.LogWarning("E-stop {Station} by {Client}.", station, Context.ConnectionId);
        dsManager.Estop(station);
        await BroadcastMatchState();
    }

    public async Task AstopStation(int stationIndex)
    {
        var station = AllianceStations.All[stationIndex];
        logger.LogWarning("A-stop {Station} by {Client}.", station, Context.ConnectionId);
        dsManager.Astop(station);
        await BroadcastMatchState();
    }

    public async Task BypassStation(int stationIndex, bool bypassed)
    {
        var station = AllianceStations.All[stationIndex];
        logger.LogInformation("{Action} bypass on {Station} by {Client}.",
            bypassed ? "Set" : "Clear", station, Context.ConnectionId);
        dsManager.SetBypass(station, bypassed);
        await BroadcastMatchState();
    }

    // ── State push ─────────────────────────────────────────────────────────────

    public Task RequestMatchState() => BroadcastMatchState();

    private Task BroadcastMatchState() => broadcaster.BroadcastAsync();
}
