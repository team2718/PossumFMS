using Microsoft.AspNetCore.SignalR;
using PossumFMS.Core.Arena;
using PossumFMS.Core.Database;
using PossumFMS.Core.Display;
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
    FieldHardwareManager fieldHardwareManager,
    DisplayManager       displayManager,
    DatabaseService      databaseService) : BackgroundService
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
        var teams = databaseService.GetTeams();

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
        matchId       = arena.MatchId,
        matchDurations = new
        {
            autoSeconds = arena.AutoDuration.TotalSeconds,
            autoToTeleopTransitionSeconds = arena.AutoToTeleopTransitionDuration.TotalSeconds,
            teleopSeconds = arena.TeleopDuration.TotalSeconds,
        },
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
        audienceView  = displayManager.AudienceView,
        allianceOrder = displayManager.AllianceOrder,
        lastCommittedMatch = BuildLastCommittedMatchObject(displayManager.LastCommittedMatch),
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
                isReady       = ds.IsReady,
                isReadyInMatch = ds.IsReadyInMatch,
                avatarBase64  = teams.TryGetValue(ds.TeamNumber, out var teamRecord) ? teamRecord.AvatarBase64 : null,
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
                fuelCount = hub.FuelCount,
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

    private static object? BuildLastCommittedMatchObject(MatchResultRecord? match)
    {
        if (match is null) return null;

        return new
        {
            matchId     = match.MatchId,
            matchType   = match.MatchType,
            matchNumber = match.MatchNumber,
            committedAt = match.CommittedAt,
            redTeams    = match.RedTeams,
            blueTeams   = match.BlueTeams,
            redTeamNicknames  = match.RedTeamNicknames,
            blueTeamNicknames = match.BlueTeamNicknames,
            redTeamAvatars    = match.RedTeamAvatars,
            blueTeamAvatars   = match.BlueTeamAvatars,
            redScore  = match.RedScore,
            blueScore = match.BlueScore,
            redBreakdown = new
            {
                autoFuelPoints    = match.RedBreakdown.AutoFuelPoints,
                autoTowerPoints   = match.RedBreakdown.AutoTowerPoints,
                teleopFuelPoints  = match.RedBreakdown.TeleopFuelPoints,
                teleopTowerPoints = match.RedBreakdown.TeleopTowerPoints,
                total             = match.RedBreakdown.Total,
            },
            blueBreakdown = new
            {
                autoFuelPoints    = match.BlueBreakdown.AutoFuelPoints,
                autoTowerPoints   = match.BlueBreakdown.AutoTowerPoints,
                teleopFuelPoints  = match.BlueBreakdown.TeleopFuelPoints,
                teleopTowerPoints = match.BlueBreakdown.TeleopTowerPoints,
                total             = match.BlueBreakdown.Total,
            },
            redRankingPoints = new
            {
                energized    = match.RedRankingPoints.Energized,
                supercharged = match.RedRankingPoints.Supercharged,
                traversal    = match.RedRankingPoints.Traversal,
                winTie       = match.RedRankingPoints.WinTie,
                total        = match.RedRankingPoints.Total,
            },
            blueRankingPoints = new
            {
                energized    = match.BlueRankingPoints.Energized,
                supercharged = match.BlueRankingPoints.Supercharged,
                traversal    = match.BlueRankingPoints.Traversal,
                winTie       = match.BlueRankingPoints.WinTie,
                total        = match.BlueRankingPoints.Total,
            },
        };
    }
}
