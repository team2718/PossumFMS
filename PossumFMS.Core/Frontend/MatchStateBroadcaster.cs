using Microsoft.AspNetCore.SignalR;
using PossumFMS.Core.Arena;
using PossumFMS.Core.Database;
using PossumFMS.Core.Display;
using PossumFMS.Core.DriverStation;
using PossumFMS.Core.FieldHardware;
using PossumFMS.Core.Frontend.Dtos;
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

    internal MatchStateDto Build()
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

        return new MatchStateDto(
            Phase: arena.Phase.ToString(),
            FreePracticeEnabled: arena.FreePracticeEnabled,
            MatchType: arena.MatchType.ToString(),
            MatchNumber: arena.MatchNumber,
            MatchId: arena.MatchId,
            MatchDurations: new MatchDurationsDto(
                AutoSeconds: arena.AutoDuration.TotalSeconds,
                AutoToTeleopTransitionSeconds: arena.AutoToTeleopTransitionDuration.TotalSeconds,
                TeleopSeconds: arena.TeleopDuration.TotalSeconds),
            TimeRemaining: arena.TimeRemaining.TotalSeconds,
            ArenaEstop: arena.ArenaEstop,
            WasAborted: arena.WasAborted,
            RedScore: gameLogic.RedScore.Total,
            BlueScore: gameLogic.BlueScore.Total,
            CurrentTeleopPeriod: gameLogic.CurrentTeleopPeriod.ToString(),
            RedBreakdown: new AllianceScoreBreakdownDto(
                AutoFuelPoints: gameLogic.RedScore.AutoFuelPoints,
                AutoTowerPoints: gameLogic.RedScore.AutoTowerPoints,
                TeleopFuelPoints: gameLogic.RedScore.TeleopFuelPoints,
                TeleopTowerPoints: gameLogic.RedScore.TeleopTowerPoints,
                PenaltyPoints: gameLogic.RedScore.PenaltyPoints,
                FuelCombined: redFuelCombined,
                TowerCombined: redTowerCombined,
                Total: gameLogic.RedScore.Total),
            BlueBreakdown: new AllianceScoreBreakdownDto(
                AutoFuelPoints: gameLogic.BlueScore.AutoFuelPoints,
                AutoTowerPoints: gameLogic.BlueScore.AutoTowerPoints,
                TeleopFuelPoints: gameLogic.BlueScore.TeleopFuelPoints,
                TeleopTowerPoints: gameLogic.BlueScore.TeleopTowerPoints,
                PenaltyPoints: gameLogic.BlueScore.PenaltyPoints,
                FuelCombined: blueFuelCombined,
                TowerCombined: blueTowerCombined,
                Total: gameLogic.BlueScore.Total),
            Violations: gameLogic.Violations
                .OrderByDescending(v => v.RecordedAt)
                .Select(BuildViolationDto)
                .ToList(),
            StationClimbs: AllianceStations.All.Select(s => new StationClimbDto(
                AutoClimbed: gameLogic.GetAutoTowerClimbed(s),
                EndgameLevel: gameLogic.GetEndgameTowerLevel(s).ToString()))
                .ToList(),
            RankingPoints: new RankingPointsDto(
                Red: BuildRankingPointBreakdown(redFuelCombined, redTowerCombined, redWins, tie),
                Blue: BuildRankingPointBreakdown(blueFuelCombined, blueTowerCombined, blueWins, tie)),
            HubActive: new HubActiveDto(
                Red: gameLogic.IsHubStrictlyActive(AllianceColor.Red),
                Blue: gameLogic.IsHubStrictlyActive(AllianceColor.Blue)),
            LoopTiming: new LoopTimingDto(
                CurrentMs: loopTiming.CurrentMs,
                MaxMs30s: loopTiming.MaxMs30s),
            AccessPoint: new AccessPointDto(
                Status: apManager.ApStatus),
            AudienceView: displayManager.AudienceView,
            AllianceOrder: displayManager.AllianceOrder,
            LastCommittedMatch: BuildLastCommittedMatchDto(displayManager.LastCommittedMatch),
            Stations: AllianceStations.All.Select((s, i) =>
            {
                var ds = dsManager[s];
                var wifi = apManager.StationStatuses[i];
                return new StationStatusDto(
                    Index: i,
                    Alliance: s.Color.ToString(),
                    Position: (int)s.Position,
                    TeamNumber: ds.TeamNumber,
                    DsLinked: ds.DsLinked,
                    RobotLinked: ds.RobotLinked,
                    RadioLinked: ds.RadioLinked,
                    RioLinked: ds.RioLinked,
                    Battery: ds.BatteryVoltage,
                    TripTimeMs: ds.DsRobotTripTimeMs,
                    MissedPackets: ds.MissedPacketCount,
                    SecondsSinceLastRobotLink: ds.SecondsSinceLastRobotLink,
                    Estop: ds.Estop,
                    Astop: ds.Astop,
                    Bypassed: ds.Bypassed,
                    WrongStation: ds.WrongStation,
                    IsReady: ds.IsReady,
                    IsReadyInMatch: ds.IsReadyInMatch,
                    AvatarBase64: teams.TryGetValue(ds.TeamNumber, out var teamRecord) ? teamRecord.AvatarBase64 : null,
                    Wifi: new WifiStatusDto(
                        RadioLinked: wifi.RadioLinked,
                        BandwidthMbps: wifi.BandwidthUsedMbps,
                        RxRateMbps: wifi.RxRateMbps,
                        TxRateMbps: wifi.TxRateMbps,
                        Snr: wifi.SignalNoiseRatio,
                        ConnectionQuality: wifi.ConnectionQuality));
            }).ToList(),
            FieldDevices: fieldHardwareManager.Devices
                .OrderBy(d => d.Type)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new FieldDeviceDto(
                    Id: d.Id,
                    Name: d.Name,
                    Type: d.Type.ToString(),
                    Status: d.Status.ToString(),
                    Bypassed: d.Bypassed,
                    LastSeenUtc: d.LastSeen,
                    SecondsSinceLastSeen: Math.Max(0, (nowUtc - d.LastSeen).TotalSeconds),
                    LastReplyTimeMs: d.LastReplyTimeMs,
                    ReplyTimeStats: new FieldDeviceReplyTimeStatsDto(
                        SampleCount: d.ReplySampleCount,
                        MinMs: d.ReplyTimeMinMs,
                        MaxMs: d.ReplyTimeMaxMs,
                        AvgMs: d.ReplyTimeAverageMs,
                        StdDevMs: d.ReplyTimeStdDevMs),
                    Heartbeat: BuildHeartbeatDiagnostics(d.LastHeartbeat)))
                .ToList());
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

    internal static RankingPointBreakdownDto BuildRankingPointBreakdown(int fuelCombined, int towerCombined, bool winsMatch, bool tiedMatch)
    {
        var energized = fuelCombined >= 100;
        var supercharged = fuelCombined >= 360;
        var traversal = towerCombined >= 50;
        var winTie = winsMatch ? 3 : tiedMatch ? 1 : 0;

        return new RankingPointBreakdownDto(
            Energized: energized,
            Supercharged: supercharged,
            Traversal: traversal,
            WinTie: winTie,
            Total: (energized ? 1 : 0) + (supercharged ? 1 : 0) + (traversal ? 1 : 0) + winTie);
    }

    private static LastCommittedMatchDto? BuildLastCommittedMatchDto(MatchResultRecord? match)
    {
        if (match is null) return null;

        return new LastCommittedMatchDto(
            MatchId: match.MatchId,
            MatchType: match.MatchType,
            MatchNumber: match.MatchNumber,
            CommittedAt: match.CommittedAt,
            RedTeams: match.RedTeams,
            BlueTeams: match.BlueTeams,
            RedTeamNicknames: match.RedTeamNicknames,
            BlueTeamNicknames: match.BlueTeamNicknames,
            RedTeamAvatars: match.RedTeamAvatars,
            BlueTeamAvatars: match.BlueTeamAvatars,
            RedScore: match.RedScore,
            BlueScore: match.BlueScore,
            Violations: match.Violations
                .OrderByDescending(v => v.RecordedAt)
                .Select(BuildViolationDto)
                .ToList(),
            RedBreakdown: new ScoreBreakdownSummaryDto(
                AutoFuelPoints: match.RedBreakdown.AutoFuelPoints,
                AutoTowerPoints: match.RedBreakdown.AutoTowerPoints,
                TeleopFuelPoints: match.RedBreakdown.TeleopFuelPoints,
                TeleopTowerPoints: match.RedBreakdown.TeleopTowerPoints,
                PenaltyPoints: match.RedBreakdown.PenaltyPoints,
                Total: match.RedBreakdown.Total),
            BlueBreakdown: new ScoreBreakdownSummaryDto(
                AutoFuelPoints: match.BlueBreakdown.AutoFuelPoints,
                AutoTowerPoints: match.BlueBreakdown.AutoTowerPoints,
                TeleopFuelPoints: match.BlueBreakdown.TeleopFuelPoints,
                TeleopTowerPoints: match.BlueBreakdown.TeleopTowerPoints,
                PenaltyPoints: match.BlueBreakdown.PenaltyPoints,
                Total: match.BlueBreakdown.Total),
            RedRankingPoints: new CommittedRankingPointsDto(
                Energized: match.RedRankingPoints.Energized,
                Supercharged: match.RedRankingPoints.Supercharged,
                Traversal: match.RedRankingPoints.Traversal,
                WinTie: match.RedRankingPoints.WinTie,
                Total: match.RedRankingPoints.Total),
            BlueRankingPoints: new CommittedRankingPointsDto(
                Energized: match.BlueRankingPoints.Energized,
                Supercharged: match.BlueRankingPoints.Supercharged,
                Traversal: match.BlueRankingPoints.Traversal,
                WinTie: match.BlueRankingPoints.WinTie,
                Total: match.BlueRankingPoints.Total));
    }

    private static MatchViolationDto BuildViolationDto(MatchViolation violation)
    {
        return new MatchViolationDto(
            Id: violation.Id.ToString(),
            StationIndex: GetStationIndex(violation.Station.Color.ToString(), (int)violation.Station.Position),
            Alliance: violation.PenalizedAlliance.ToString(),
            Position: (int)violation.Station.Position,
            TeamNumber: violation.TeamNumber,
            Rule: violation.Rule,
            Type: violation.Type.ToString(),
            Phase: violation.Phase.ToString(),
            TimeRemainingSeconds: violation.TimeRemainingSeconds,
            RecordedAt: violation.RecordedAt,
            AwardedPoints: violation.AwardedPoints,
            AwardedToAlliance: violation.AwardedToAlliance.ToString());
    }

    private static MatchViolationDto BuildViolationDto(MatchViolationRecord violation)
    {
        return new MatchViolationDto(
            Id: violation.Id,
            StationIndex: GetStationIndex(violation.Alliance, violation.Position),
            Alliance: violation.Alliance,
            Position: violation.Position,
            TeamNumber: violation.TeamNumber,
            Rule: violation.Rule,
            Type: violation.Type,
            Phase: violation.Phase,
            TimeRemainingSeconds: violation.TimeRemainingSeconds,
            RecordedAt: violation.RecordedAt,
            AwardedPoints: violation.AwardedPoints,
            AwardedToAlliance: violation.AwardedToAlliance);
    }

    private static int GetStationIndex(string alliance, int position)
    {
        for (var i = 0; i < AllianceStations.All.Count; i++)
        {
            var station = AllianceStations.All[i];
            if (station.Color.ToString().Equals(alliance, StringComparison.OrdinalIgnoreCase)
                && (int)station.Position == position)
            {
                return i;
            }
        }

        return -1;
    }
}
