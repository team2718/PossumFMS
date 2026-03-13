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
    AccessPointManager apManager,
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

    // ── State push ─────────────────────────────────────────────────────────────

    public async Task RequestMatchState() => await BroadcastMatchState();

    private Task BroadcastMatchState() =>
        Clients.All.SendAsync("MatchState", BuildMatchStatePayload());

    private object BuildMatchStatePayload() => new
    {
        phase         = arena.Phase.ToString(),
        matchType     = arena.MatchType.ToString(),
        matchNumber   = arena.MatchNumber,
        timeRemaining = arena.TimeRemaining.TotalSeconds,
        arenaEstop    = arena.ArenaEstop,
        accessPoint   = new
        {
            status   = apManager.ApStatus,
        },
        stations      = AllianceStations.All.Select((s, i) =>
        {
            var ds   = dsManager[s];
            var wifi = apManager.StationStatuses[i];
            return new
            {
                index        = i,
                alliance     = s.Color.ToString(),
                position     = (int)s.Position,
                teamNumber   = ds.TeamNumber,
                dsLinked     = ds.DsLinked,
                robotLinked  = ds.RobotLinked,
                radioLinked  = ds.RadioLinked,
                rioLinked    = ds.RioLinked,
                battery      = ds.BatteryVoltage,
                tripTimeMs   = ds.DsRobotTripTimeMs,
                missedPackets= ds.MissedPacketCount,
                estop        = ds.Estop,
                astop        = ds.Astop,
                bypassed     = ds.Bypassed,
                wrongStation = ds.WrongStation,
                wifi         = new
                {
                    radioLinked      = wifi.RadioLinked,
                    bandwidthMbps    = wifi.BandwidthUsedMbps,
                    rxRateMbps       = wifi.RxRateMbps,
                    txRateMbps       = wifi.TxRateMbps,
                    snr              = wifi.SignalNoiseRatio,
                    connectionQuality= wifi.ConnectionQuality,
                },
            };
        }),
    };
}
