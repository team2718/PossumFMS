namespace PossumFMS.Core.Arena;

/// <summary>Sent in control packet byte [6]. Values match the FRC DS protocol.</summary>
public enum MatchType : byte
{
    None          = 0,
    Practice      = 1,
    Qualification = 2,
    Playoff       = 3,
}

public enum MatchPhase
{
    Idle,
    Auto,
    Teleop,
    Over,
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

public static class AllianceStations
{
    public static readonly AllianceStation Red1  = new(AllianceColor.Red,  StationPosition.One);
    public static readonly AllianceStation Red2  = new(AllianceColor.Red,  StationPosition.Two);
    public static readonly AllianceStation Red3  = new(AllianceColor.Red,  StationPosition.Three);
    public static readonly AllianceStation Blue1 = new(AllianceColor.Blue, StationPosition.One);
    public static readonly AllianceStation Blue2 = new(AllianceColor.Blue, StationPosition.Two);
    public static readonly AllianceStation Blue3 = new(AllianceColor.Blue, StationPosition.Three);

    public static readonly IReadOnlyList<AllianceStation> All =
    [
        Red1, Red2, Red3,
        Blue1, Blue2, Blue3,
    ];
}
