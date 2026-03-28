namespace PossumFMS.Core.Arena;

/// <summary>Sent in control packet byte [6]. Values match the FRC DS protocol.</summary>
public enum MatchType : byte
{
    Test = 0,
    Practice = 1,
    Qualification = 2,
    Playoff = 3,
}

public enum ViolationType
{
    MinorFoul,
    MajorFoul,
    YellowCard,
    RedCard,
}

public enum MatchPhase
{
    Idle,
    PreMatch,
    Auto,
    AutoToTeleopTransition,
    Teleop,
    PostMatch,
}

public enum AllianceColor
{
    Red,
    Blue,
}

public enum StationPosition
{
    One = 1,
    Two = 2,
    Three = 3,
}

public enum TowerEndgameLevel
{
    None = 0,
    L1 = 1,
    L2 = 2,
    L3 = 3,
}

public record AllianceStation(AllianceColor Color, StationPosition Position);

public sealed record MatchViolation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AllianceStation Station { get; init; } = AllianceStations.Red1;
    public int TeamNumber { get; init; }
    public string Rule { get; init; } = "";
    public ViolationType Type { get; init; }
    public MatchPhase Phase { get; init; }
    public double TimeRemainingSeconds { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;

    public AllianceColor PenalizedAlliance => Station.Color;
    public AllianceColor AwardedToAlliance => Station.Color == AllianceColor.Red ? AllianceColor.Blue : AllianceColor.Red;
    public int AwardedPoints => ViolationRules.GetAwardedPoints(Type);
}

public static class ViolationRules
{
    private static readonly Dictionary<string, ViolationType> RuleToType = new(StringComparer.OrdinalIgnoreCase)
    {
        { "G403", ViolationType.MajorFoul },
        { "G405", ViolationType.MinorFoul },
        { "G407", ViolationType.MajorFoul },
        { "G408", ViolationType.MinorFoul },
        { "G415", ViolationType.MinorFoul },
        { "G416", ViolationType.MajorFoul },
        { "G417", ViolationType.MajorFoul },
        { "G418", ViolationType.MinorFoul },
        { "G420", ViolationType.MajorFoul },
    };

    public static bool IsImplementedRule(string rule)
    {
        return RuleToType.ContainsKey(rule);
    }

    public static ViolationType GetViolationTypeFromRule(string rule)
    {
        if (!RuleToType.TryGetValue(rule, out var type))
            throw new ArgumentException($"Unknown rule '{rule}'.", nameof(rule));
        return type;
    }

    public static bool IsImplementedFoul(ViolationType type)
    {
        return type is ViolationType.MinorFoul or ViolationType.MajorFoul;
    }

    public static int GetAwardedPoints(ViolationType type)
    {
        return type switch
        {
            ViolationType.MinorFoul => 5,
            ViolationType.MajorFoul => 15,
            _ => 0,
        };
    }
}

/// <summary>
/// Mutable score accumulator for one alliance during a match.
/// Add game-specific scoring fields here; call Reset() when a new match starts.
/// </summary>
public sealed class AllianceScore
{
    public int AutoFuelPoints { get; set; }
    public int AutoTowerPoints { get; set; }
    public int TeleopFuelPoints { get; set; }
    public int TeleopTowerPoints { get; set; }
    public int PenaltyPoints { get; set; }

    public int Total => AutoFuelPoints + AutoTowerPoints + TeleopFuelPoints + TeleopTowerPoints + PenaltyPoints;

    public void Reset()
    {
        AutoFuelPoints = 0;
        AutoTowerPoints = 0;
        TeleopFuelPoints = 0;
        TeleopTowerPoints = 0;
        PenaltyPoints = 0;
    }
}

/// <summary>
/// Sub-periods within Teleop for REBUILT 2026. Based on Table 6-3.
/// </summary>
public enum TeleopPeriod
{
    NotStarted,
    TransitionShift,
    Shift1,
    Shift2,
    Shift3,
    Shift4,
    EndGame,
}

public static class AllianceStations
{
    public static readonly AllianceStation Red1 = new(AllianceColor.Red, StationPosition.One);
    public static readonly AllianceStation Red2 = new(AllianceColor.Red, StationPosition.Two);
    public static readonly AllianceStation Red3 = new(AllianceColor.Red, StationPosition.Three);
    public static readonly AllianceStation Blue1 = new(AllianceColor.Blue, StationPosition.One);
    public static readonly AllianceStation Blue2 = new(AllianceColor.Blue, StationPosition.Two);
    public static readonly AllianceStation Blue3 = new(AllianceColor.Blue, StationPosition.Three);

    public static readonly IReadOnlyList<AllianceStation> All =
    [
        Red1, Red2, Red3,
        Blue1, Blue2, Blue3,
    ];
}
