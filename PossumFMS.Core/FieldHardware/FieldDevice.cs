using System.Net;
using System.Net.Sockets;

namespace PossumFMS.Core.FieldHardware;

public enum FieldDeviceType
{
    Unknown,
    Hub,
    Estop,
}

public enum FieldDeviceStatus
{
    Disconnected,
    Connected,
    Error,
}

public abstract record FieldDeviceHeartbeat(DateTime ReceivedUtc);

public sealed record HubHeartbeat(string Alliance, int FuelScored, DateTime ReceivedUtc)
    : FieldDeviceHeartbeat(ReceivedUtc);

public sealed record EstopHeartbeat(bool AstopActivated, bool EstopActivated, DateTime ReceivedUtc)
    : FieldDeviceHeartbeat(ReceivedUtc);

/// <summary>
/// Represents one connected ESP32 field device client.
/// </summary>
public sealed class FieldDevice(TcpClient client)
{
    public string Name { get; private set; } = "unknown";
    public FieldDeviceType Type { get; private set; } = FieldDeviceType.Unknown;
    public IPEndPoint? RemoteEndpoint => Client.Client.RemoteEndPoint as IPEndPoint;

    public FieldDeviceStatus Status { get; set; } = FieldDeviceStatus.Connected;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public FieldDeviceHeartbeat? LastHeartbeat { get; private set; }

    internal TcpClient Client { get; } = client;
    internal NetworkStream Stream => Client.GetStream();

    public bool IsConnected => Status == FieldDeviceStatus.Connected && Client.Connected;

    public void UpdateIdentity(string? name, string? type)
    {
        if (!string.IsNullOrWhiteSpace(name))
            Name = name.Trim();

        Type = ParseDeviceType(type);
    }

    public void ApplyHeartbeat(FieldDeviceHeartbeat heartbeat)
    {
        LastHeartbeat = heartbeat;
        Type = heartbeat switch
        {
            HubHeartbeat => FieldDeviceType.Hub,
            EstopHeartbeat => FieldDeviceType.Estop,
            _ => FieldDeviceType.Unknown,
        };
    }

    private static FieldDeviceType ParseDeviceType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return FieldDeviceType.Unknown;

        if (Enum.TryParse<FieldDeviceType>(type, ignoreCase: true, out var parsed))
            return parsed;

        return type.Trim().ToLowerInvariant() switch
        {
            "hub" => FieldDeviceType.Hub,
            "estop" => FieldDeviceType.Estop,
            _ => FieldDeviceType.Unknown,
        };
    }
}
