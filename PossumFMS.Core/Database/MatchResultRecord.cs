namespace PossumFMS.Core.Database;

/// <summary>
/// A committed match result snapshot stored to disk and cached in memory.
/// Team nicknames and avatars are denormalized at commit time so the display
/// never depends on the live team database.
/// </summary>
public sealed record MatchResultRecord
{
    public string MatchId { get; init; } = Guid.NewGuid().ToString();
    public string MatchType { get; init; } = "";
    public int MatchNumber { get; init; }
    public DateTimeOffset CommittedAt { get; init; }

    /// <summary>Team numbers for Red 1, 2, 3.</summary>
    public int[] RedTeams { get; init; } = new int[3];

    /// <summary>Team numbers for Blue 1, 2, 3.</summary>
    public int[] BlueTeams { get; init; } = new int[3];

    /// <summary>Nicknames for Red 1, 2, 3 (empty string if not in team database).</summary>
    public string[] RedTeamNicknames { get; init; } = new string[3];

    /// <summary>Nicknames for Blue 1, 2, 3.</summary>
    public string[] BlueTeamNicknames { get; init; } = new string[3];

    /// <summary>Raw base64 PNG avatars for Red 1, 2, 3 (null if unavailable).</summary>
    public string?[] RedTeamAvatars { get; init; } = new string?[3];

    /// <summary>Raw base64 PNG avatars for Blue 1, 2, 3 (null if unavailable).</summary>
    public string?[] BlueTeamAvatars { get; init; } = new string?[3];

    public int RedScore { get; init; }
    public int BlueScore { get; init; }

    public MatchScoreBreakdown RedBreakdown { get; init; } = new();
    public MatchScoreBreakdown BlueBreakdown { get; init; } = new();

    public MatchRankingPoints RedRankingPoints { get; init; } = new();
    public MatchRankingPoints BlueRankingPoints { get; init; } = new();

    public MatchViolationRecord[] Violations { get; init; } = [];
}

public sealed record MatchScoreBreakdown
{
    public int AutoFuelPoints { get; init; }
    public int AutoTowerPoints { get; init; }
    public int TeleopFuelPoints { get; init; }
    public int TeleopTowerPoints { get; init; }
    public int PenaltyPoints { get; init; }
    public int Total { get; init; }
}

public sealed record MatchRankingPoints
{
    public bool Energized { get; init; }
    public bool Supercharged { get; init; }
    public bool Traversal { get; init; }
    public int WinTie { get; init; }
    public int Total { get; init; }
}

public sealed record MatchViolationRecord
{
    public string Id { get; init; } = string.Empty;
    public string Alliance { get; init; } = string.Empty;
    public int Position { get; init; }
    public int TeamNumber { get; init; }
    public string Rule { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public double TimeRemainingSeconds { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public int AwardedPoints { get; init; }
    public string AwardedToAlliance { get; init; } = string.Empty;
}
