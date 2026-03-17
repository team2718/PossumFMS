namespace PossumFMS.Core.Arena;

/// <summary>Sent in control packet byte [6]. Values match the FRC DS protocol.</summary>
public enum MatchType : byte
{
    None = 0,
    Practice = 1,
    Qualification = 2,
    Playoff = 3,
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

public record AllianceStation(AllianceColor Color, StationPosition Position);

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

    public int Total => AutoFuelPoints + AutoTowerPoints + TeleopFuelPoints + TeleopTowerPoints;

    public void Reset()
    {
        AutoFuelPoints = 0;
        AutoTowerPoints = 0;
        TeleopFuelPoints = 0;
        TeleopTowerPoints = 0;
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
