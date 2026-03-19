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

public sealed record HubHeartbeat(string Alliance, int FuelDelta, int HeartbeatId, DateTime ReceivedUtc)
    : FieldDeviceHeartbeat(ReceivedUtc);

public sealed record EstopHeartbeat(string Alliance, int Station, bool AstopActivated, bool EstopActivated, DateTime ReceivedUtc)
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
    public int LastReplyTimeMs { get; set; }
    public int ReplySampleCount { get; private set; }
    public int ReplyTimeMinMs { get; private set; }
    public int ReplyTimeMaxMs { get; private set; }
    public double ReplyTimeAverageMs { get; private set; }
    public double ReplyTimeStdDevMs { get; private set; }

    private double _replyTimeM2;

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

    public void UpdateLastReplyTime(int replyTimeMs)
    {
        var clampedReplyTimeMs = Math.Max(0, replyTimeMs);
        LastReplyTimeMs = clampedReplyTimeMs;

        ReplySampleCount++;

        if (ReplySampleCount == 1)
        {
            ReplyTimeMinMs = clampedReplyTimeMs;
            ReplyTimeMaxMs = clampedReplyTimeMs;
            ReplyTimeAverageMs = clampedReplyTimeMs;
            ReplyTimeStdDevMs = 0;
            _replyTimeM2 = 0;
            return;
        }

        ReplyTimeMinMs = Math.Min(ReplyTimeMinMs, clampedReplyTimeMs);
        ReplyTimeMaxMs = Math.Max(ReplyTimeMaxMs, clampedReplyTimeMs);

        var delta = clampedReplyTimeMs - ReplyTimeAverageMs;
        ReplyTimeAverageMs += delta / ReplySampleCount;
        var delta2 = clampedReplyTimeMs - ReplyTimeAverageMs;
        _replyTimeM2 += delta * delta2;

        ReplyTimeStdDevMs = Math.Sqrt(_replyTimeM2 / ReplySampleCount);
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
