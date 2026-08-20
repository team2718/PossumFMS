using System.Text.Json.Serialization;

namespace PossumFMS.Core.Frontend.Dtos;

public sealed record MatchStateDto(
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("freePracticeEnabled")] bool FreePracticeEnabled,
    [property: JsonPropertyName("matchType")] string MatchType,
    [property: JsonPropertyName("matchNumber")] int MatchNumber,
    [property: JsonPropertyName("matchId")] string MatchId,
    [property: JsonPropertyName("matchDurations")] MatchDurationsDto MatchDurations,
    [property: JsonPropertyName("timeRemaining")] double TimeRemaining,
    [property: JsonPropertyName("arenaEstop")] bool ArenaEstop,
    [property: JsonPropertyName("wasAborted")] bool WasAborted,
    [property: JsonPropertyName("redScore")] int RedScore,
    [property: JsonPropertyName("blueScore")] int BlueScore,
    [property: JsonPropertyName("currentTeleopPeriod")] string CurrentTeleopPeriod,
    [property: JsonPropertyName("redBreakdown")] AllianceScoreBreakdownDto RedBreakdown,
    [property: JsonPropertyName("blueBreakdown")] AllianceScoreBreakdownDto BlueBreakdown,
    [property: JsonPropertyName("violations")] IReadOnlyList<MatchViolationDto> Violations,
    [property: JsonPropertyName("stationClimbs")] IReadOnlyList<StationClimbDto> StationClimbs,
    [property: JsonPropertyName("rankingPoints")] RankingPointsDto RankingPoints,
    [property: JsonPropertyName("hubActive")] HubActiveDto HubActive,
    [property: JsonPropertyName("loopTiming")] LoopTimingDto LoopTiming,
    [property: JsonPropertyName("accessPoint")] AccessPointDto AccessPoint,
    [property: JsonPropertyName("audienceView")] string AudienceView,
    [property: JsonPropertyName("allianceOrder")] string AllianceOrder,
    [property: JsonPropertyName("lastCommittedMatch")] LastCommittedMatchDto? LastCommittedMatch,
    [property: JsonPropertyName("stations")] IReadOnlyList<StationStatusDto> Stations,
    [property: JsonPropertyName("fieldDevices")] IReadOnlyList<FieldDeviceDto> FieldDevices);

public sealed record MatchDurationsDto(
    [property: JsonPropertyName("autoSeconds")] double AutoSeconds,
    [property: JsonPropertyName("autoToTeleopTransitionSeconds")] double AutoToTeleopTransitionSeconds,
    [property: JsonPropertyName("teleopSeconds")] double TeleopSeconds);

public sealed record AllianceScoreBreakdownDto(
    [property: JsonPropertyName("autoFuelPoints")] int AutoFuelPoints,
    [property: JsonPropertyName("autoTowerPoints")] int AutoTowerPoints,
    [property: JsonPropertyName("teleopFuelPoints")] int TeleopFuelPoints,
    [property: JsonPropertyName("teleopTowerPoints")] int TeleopTowerPoints,
    [property: JsonPropertyName("penaltyPoints")] int PenaltyPoints,
    [property: JsonPropertyName("fuelCombined")] int FuelCombined,
    [property: JsonPropertyName("towerCombined")] int TowerCombined,
    [property: JsonPropertyName("total")] int Total);

public sealed record RankingPointsDto(
    [property: JsonPropertyName("red")] RankingPointBreakdownDto Red,
    [property: JsonPropertyName("blue")] RankingPointBreakdownDto Blue);

public sealed record RankingPointBreakdownDto(
    [property: JsonPropertyName("energized")] bool Energized,
    [property: JsonPropertyName("supercharged")] bool Supercharged,
    [property: JsonPropertyName("traversal")] bool Traversal,
    [property: JsonPropertyName("winTie")] int WinTie,
    [property: JsonPropertyName("total")] int Total);

public sealed record HubActiveDto(
    [property: JsonPropertyName("red")] bool Red,
    [property: JsonPropertyName("blue")] bool Blue);

public sealed record LoopTimingDto(
    [property: JsonPropertyName("currentMs")] double CurrentMs,
    [property: JsonPropertyName("maxMs30s")] double MaxMs30s);

public sealed record AccessPointDto(
    [property: JsonPropertyName("status")] string Status);

public sealed record StationClimbDto(
    [property: JsonPropertyName("autoClimbed")] bool AutoClimbed,
    [property: JsonPropertyName("endgameLevel")] string EndgameLevel);

public sealed record WifiStatusDto(
    [property: JsonPropertyName("radioLinked")] bool RadioLinked,
    [property: JsonPropertyName("bandwidthMbps")] double BandwidthMbps,
    [property: JsonPropertyName("rxRateMbps")] double RxRateMbps,
    [property: JsonPropertyName("txRateMbps")] double TxRateMbps,
    [property: JsonPropertyName("snr")] int Snr,
    [property: JsonPropertyName("connectionQuality")] int ConnectionQuality);

public sealed record StationStatusDto(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("alliance")] string Alliance,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("teamNumber")] int TeamNumber,
    [property: JsonPropertyName("dsLinked")] bool DsLinked,
    [property: JsonPropertyName("robotLinked")] bool RobotLinked,
    [property: JsonPropertyName("radioLinked")] bool RadioLinked,
    [property: JsonPropertyName("rioLinked")] bool RioLinked,
    [property: JsonPropertyName("battery")] double Battery,
    [property: JsonPropertyName("tripTimeMs")] int TripTimeMs,
    [property: JsonPropertyName("missedPackets")] int MissedPackets,
    [property: JsonPropertyName("secondsSinceLastRobotLink")] double SecondsSinceLastRobotLink,
    [property: JsonPropertyName("estop")] bool Estop,
    [property: JsonPropertyName("astop")] bool Astop,
    [property: JsonPropertyName("bypassed")] bool Bypassed,
    [property: JsonPropertyName("wrongStation")] string WrongStation,
    [property: JsonPropertyName("isReady")] bool IsReady,
    [property: JsonPropertyName("isReadyInMatch")] bool IsReadyInMatch,
    [property: JsonPropertyName("avatarBase64")] string? AvatarBase64,
    [property: JsonPropertyName("wifi")] WifiStatusDto? Wifi);

public sealed record FieldDeviceReplyTimeStatsDto(
    [property: JsonPropertyName("sampleCount")] int SampleCount,
    [property: JsonPropertyName("minMs")] double MinMs,
    [property: JsonPropertyName("maxMs")] double MaxMs,
    [property: JsonPropertyName("avgMs")] double AvgMs,
    [property: JsonPropertyName("stdDevMs")] double StdDevMs);

public sealed record FieldDeviceDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("bypassed")] bool Bypassed,
    [property: JsonPropertyName("lastSeenUtc")] DateTime LastSeenUtc,
    [property: JsonPropertyName("secondsSinceLastSeen")] double SecondsSinceLastSeen,
    [property: JsonPropertyName("lastReplyTimeMs")] double LastReplyTimeMs,
    [property: JsonPropertyName("replyTimeStats")] FieldDeviceReplyTimeStatsDto ReplyTimeStats,
    [property: JsonPropertyName("heartbeat")] object? Heartbeat);

public sealed record MatchViolationDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("stationIndex")] int StationIndex,
    [property: JsonPropertyName("alliance")] string Alliance,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("teamNumber")] int TeamNumber,
    [property: JsonPropertyName("rule")] string Rule,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("timeRemainingSeconds")] double TimeRemainingSeconds,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("awardedPoints")] int AwardedPoints,
    [property: JsonPropertyName("awardedToAlliance")] string AwardedToAlliance);

public sealed record LastCommittedMatchDto(
    [property: JsonPropertyName("matchId")] string MatchId,
    [property: JsonPropertyName("matchType")] string MatchType,
    [property: JsonPropertyName("matchNumber")] int MatchNumber,
    [property: JsonPropertyName("committedAt")] DateTimeOffset CommittedAt,
    [property: JsonPropertyName("redTeams")] int[] RedTeams,
    [property: JsonPropertyName("blueTeams")] int[] BlueTeams,
    [property: JsonPropertyName("redTeamNicknames")] string[] RedTeamNicknames,
    [property: JsonPropertyName("blueTeamNicknames")] string[] BlueTeamNicknames,
    [property: JsonPropertyName("redTeamAvatars")] string?[] RedTeamAvatars,
    [property: JsonPropertyName("blueTeamAvatars")] string?[] BlueTeamAvatars,
    [property: JsonPropertyName("redScore")] int RedScore,
    [property: JsonPropertyName("blueScore")] int BlueScore,
    [property: JsonPropertyName("violations")] IReadOnlyList<MatchViolationDto> Violations,
    [property: JsonPropertyName("redBreakdown")] ScoreBreakdownSummaryDto RedBreakdown,
    [property: JsonPropertyName("blueBreakdown")] ScoreBreakdownSummaryDto BlueBreakdown,
    [property: JsonPropertyName("redRankingPoints")] CommittedRankingPointsDto RedRankingPoints,
    [property: JsonPropertyName("blueRankingPoints")] CommittedRankingPointsDto BlueRankingPoints);

public sealed record ScoreBreakdownSummaryDto(
    [property: JsonPropertyName("autoFuelPoints")] int AutoFuelPoints,
    [property: JsonPropertyName("autoTowerPoints")] int AutoTowerPoints,
    [property: JsonPropertyName("teleopFuelPoints")] int TeleopFuelPoints,
    [property: JsonPropertyName("teleopTowerPoints")] int TeleopTowerPoints,
    [property: JsonPropertyName("penaltyPoints")] int PenaltyPoints,
    [property: JsonPropertyName("total")] int Total);

public sealed record CommittedRankingPointsDto(
    [property: JsonPropertyName("energized")] bool Energized,
    [property: JsonPropertyName("supercharged")] bool Supercharged,
    [property: JsonPropertyName("traversal")] bool Traversal,
    [property: JsonPropertyName("winTie")] int WinTie,
    [property: JsonPropertyName("total")] int Total);

