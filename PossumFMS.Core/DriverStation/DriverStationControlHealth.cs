namespace PossumFMS.Core.DriverStation;

public enum DriverStationControlState
{
    Stopped,
    Starting,
    Operational,
    Faulted,
}

public sealed record DriverStationControlHealth(
    DriverStationControlState State,
    bool TransportAvailable,
    DateTime? LastFaultUtc,
    string? LastFault);