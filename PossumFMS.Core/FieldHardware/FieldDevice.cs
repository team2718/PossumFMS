using System.Net.Sockets;

namespace PossumFMS.Core.FieldHardware;

public enum FieldDeviceType
{
    /// <summary>Generic ESP32 field device.</summary>
    Generic,
}

public enum FieldDeviceStatus
{
    Disconnected,
    Connected,
    Error,
}

/// <summary>
/// Represents a single ESP32 field device. FieldHardwareManager keeps one
/// instance per configured device and maintains the TCP connection to it.
/// </summary>
public sealed class FieldDevice(string name, string host, int port, FieldDeviceType type = FieldDeviceType.Generic)
{
    public string           Name   { get; } = name;
    public string           Host   { get; } = host;
    public int              Port   { get; } = port;
    public FieldDeviceType  Type   { get; } = type;

    public FieldDeviceStatus Status    { get; set; } = FieldDeviceStatus.Disconnected;
    public DateTime          LastSeen  { get; set; } = DateTime.MinValue;

    internal TcpClient? Client { get; set; }
    internal NetworkStream? Stream => Client?.GetStream();

    public bool IsConnected => Status == FieldDeviceStatus.Connected && Client?.Connected == true;
}
