using Microsoft.AspNetCore.SignalR;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using PossumFMS.Core.FieldHardware;
using PossumFMS.Core.Network;

namespace PossumFMS.Core.Frontend;

/// <summary>
/// Periodically pushes MatchState to all connected SignalR clients while a
/// match is active, so the timer and station indicators stay live without
/// requiring any button press from the operator.
///
/// FmsHub calls BroadcastAsync() directly for immediate updates (e.g. after
/// phase transitions or team assignments); this service supplements those
/// calls with a 500 ms heartbeat during Auto/Teleop so the countdown ticks
/// smoothly in the browser.
/// </summary>
public sealed class MatchStateBroadcaster(
    IHubContext<FmsHub> hubContext,
    Arena.Arena         arena,
    GameLogic            gameLogic,
    DriverStationManager dsManager,
    AccessPointManager   apManager,
    FieldHardwareManager fieldHardwareManager) : BackgroundService
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Push the current state snapshot to all connected clients immediately.</summary>
    public Task BroadcastAsync() =>
        hubContext.Clients.All.SendAsync("MatchState", Build());

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(BroadcastInterval, ct);
            await BroadcastAsync();
        }
    }

    internal object Build()
    {
        var loopTiming = dsManager.GetLoopTimingSnapshot();

        var redFuelCombined = gameLogic.RedScore.AutoFuelPoints + gameLogic.RedScore.TeleopFuelPoints;
        var blueFuelCombined = gameLogic.BlueScore.AutoFuelPoints + gameLogic.BlueScore.TeleopFuelPoints;
        var redTowerCombined = gameLogic.RedScore.AutoTowerPoints + gameLogic.RedScore.TeleopTowerPoints;
        var blueTowerCombined = gameLogic.BlueScore.AutoTowerPoints + gameLogic.BlueScore.TeleopTowerPoints;

        var redWins = gameLogic.RedScore.Total > gameLogic.BlueScore.Total;
        var blueWins = gameLogic.BlueScore.Total > gameLogic.RedScore.Total;
        var tie = gameLogic.RedScore.Total == gameLogic.BlueScore.Total;
        var nowUtc = DateTime.UtcNow;

        return new
        {
        phase         = arena.Phase.ToString(),
        freePracticeEnabled = arena.FreePracticeEnabled,
        matchType     = arena.MatchType.ToString(),
        matchNumber   = arena.MatchNumber,
        timeRemaining = arena.TimeRemaining.TotalSeconds,
        arenaEstop    = arena.ArenaEstop,
        wasAborted    = arena.WasAborted,
        redScore      = gameLogic.RedScore.Total,
        blueScore     = gameLogic.BlueScore.Total,
        currentTeleopPeriod = gameLogic.CurrentTeleopPeriod.ToString(),
        redBreakdown  = new
        {
            autoFuelPoints = gameLogic.RedScore.AutoFuelPoints,
            autoTowerPoints = gameLogic.RedScore.AutoTowerPoints,
            teleopFuelPoints = gameLogic.RedScore.TeleopFuelPoints,
            teleopTowerPoints = gameLogic.RedScore.TeleopTowerPoints,
            fuelCombined = redFuelCombined,
            towerCombined = redTowerCombined,
            total = gameLogic.RedScore.Total,
        },
        blueBreakdown = new
        {
            autoFuelPoints = gameLogic.BlueScore.AutoFuelPoints,
            autoTowerPoints = gameLogic.BlueScore.AutoTowerPoints,
            teleopFuelPoints = gameLogic.BlueScore.TeleopFuelPoints,
            teleopTowerPoints = gameLogic.BlueScore.TeleopTowerPoints,
            fuelCombined = blueFuelCombined,
            towerCombined = blueTowerCombined,
            total = gameLogic.BlueScore.Total,
        },
        stationClimbs = AllianceStations.All.Select(s => new
        {
            autoClimbed = gameLogic.GetAutoTowerClimbed(s),
            endgameLevel = gameLogic.GetEndgameTowerLevel(s).ToString(),
        }),
        rankingPoints = new
        {
            red = BuildRankingPointBreakdown(redFuelCombined, redTowerCombined, redWins, tie),
            blue = BuildRankingPointBreakdown(blueFuelCombined, blueTowerCombined, blueWins, tie),
        },
        hubActive = new
        {
            red  = gameLogic.IsHubStrictlyActive(AllianceColor.Red),
            blue = gameLogic.IsHubStrictlyActive(AllianceColor.Blue),
        },
        loopTiming    = new
        {
            currentMs = loopTiming.CurrentMs,
            maxMs30s  = loopTiming.MaxMs30s,
        },
        accessPoint   = new { status = apManager.ApStatus },
        stations      = AllianceStations.All.Select((s, i) =>
        {
            var ds   = dsManager[s];
            var wifi = apManager.StationStatuses[i];
            return new
            {
                index         = i,
                alliance      = s.Color.ToString(),
                position      = (int)s.Position,
                teamNumber    = ds.TeamNumber,
                dsLinked      = ds.DsLinked,
                robotLinked   = ds.RobotLinked,
                radioLinked   = ds.RadioLinked,
                rioLinked     = ds.RioLinked,
                battery       = ds.BatteryVoltage,
                tripTimeMs    = ds.DsRobotTripTimeMs,
                missedPackets = ds.MissedPacketCount,
                secondsSinceLastRobotLink = ds.SecondsSinceLastRobotLink,
                estop         = ds.Estop,
                astop         = ds.Astop,
                bypassed      = ds.Bypassed,
                wrongStation  = ds.WrongStation,
                wifi          = new
                {
                    radioLinked       = wifi.RadioLinked,
                    bandwidthMbps     = wifi.BandwidthUsedMbps,
                    rxRateMbps        = wifi.RxRateMbps,
                    txRateMbps        = wifi.TxRateMbps,
                    snr               = wifi.SignalNoiseRatio,
                    connectionQuality = wifi.ConnectionQuality,
                },
            };
        }),
        fieldDevices  = fieldHardwareManager.Devices
            .OrderBy(d => d.Type)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new
            {
                id = d.Id,
                name = d.Name,
                type = d.Type.ToString(),
                status = d.Status.ToString(),
                bypassed = d.Bypassed,
                lastSeenUtc = d.LastSeen,
                secondsSinceLastSeen = Math.Max(0, (nowUtc - d.LastSeen).TotalSeconds),
                lastReplyTimeMs = d.LastReplyTimeMs,
                replyTimeStats = new
                {
                    sampleCount = d.ReplySampleCount,
                    minMs = d.ReplyTimeMinMs,
                    maxMs = d.ReplyTimeMaxMs,
                    avgMs = d.ReplyTimeAverageMs,
                    stdDevMs = d.ReplyTimeStdDevMs,
                },
                heartbeat = BuildHeartbeatDiagnostics(d.LastHeartbeat),
            }),
    };
    }

    private static object? BuildHeartbeatDiagnostics(FieldDeviceHeartbeat? heartbeat)
    {
        return heartbeat switch
        {
            HubHeartbeat hub => new
            {
                kind = "Hub",
                receivedUtc = hub.ReceivedUtc,
                alliance = hub.Alliance,
                fuelDelta = hub.FuelDelta,
                heartbeatId = hub.HeartbeatId,
            },
            EstopHeartbeat estop => new
            {
                kind = "Estop",
                receivedUtc = estop.ReceivedUtc,
                field = estop.Alliance,
                station = estop.Station,
                astopActivated = estop.AstopActivated,
                estopActivated = estop.EstopActivated,
            },
            _ => null,
        };
    }

    internal static object BuildRankingPointBreakdown(int fuelCombined, int towerCombined, bool winsMatch, bool tiedMatch)
    {
        var energized = fuelCombined >= 100;
        var supercharged = fuelCombined >= 360;
        var traversal = towerCombined >= 50;
        var winTie = winsMatch ? 3 : tiedMatch ? 1 : 0;

        return new
        {
            energized,
            supercharged,
            traversal,
            winTie,
            total = (energized ? 1 : 0) + (supercharged ? 1 : 0) + (traversal ? 1 : 0) + winTie,
        };
    }
}
