using System.Net;
using System.Net.Sockets;
using PossumFMS.Core.Arena;

namespace PossumFMS.Core.DriverStation;

/// <summary>
/// Runtime state for a single driver station slot. Updated by DriverStationManager
/// as UDP packets arrive and the TCP connection is managed.
/// </summary>
public sealed class DriverStationConnection
{
    public AllianceStation Station { get; }

    // ── Team assignment ────────────────────────────────────────────────────────

    public int    TeamNumber    { get; internal set; }
    public string WpaKey        { get; internal set; } = string.Empty;

    /// <summary>
    /// Non-empty when the robot connected to this station belongs to a team
    /// that is assigned to a different station (wrong-plug detection via IP).
    /// </summary>
    public string WrongStation  { get; internal set; } = string.Empty;

    // ── Network ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Endpoint used to send UDP control packets (DS_IP:1121).
    /// Set from the TCP connection's remote IP; null until DS connects via TCP.
    /// </summary>
    internal IPEndPoint? UdpEndpoint { get; set; }

    internal TcpClient? TcpClient { get; set; }

    // ── Link status ────────────────────────────────────────────────────────────

    public bool DsLinked    { get; internal set; }
    public bool RadioLinked { get; internal set; }
    public bool RioLinked   { get; internal set; }
    public bool RobotLinked { get; internal set; }

    /// <summary>Battery voltage reported by the robot (volts). Zero when robot is not linked.</summary>
    public double BatteryVoltage { get; internal set; }

    /// <summary>Missed DS→robot packet count, reported via UDP tag.</summary>
    public int MissedPacketCount  { get; internal set; }

    /// <summary>DS→robot round-trip time in milliseconds, reported via UDP tag.</summary>
    public int DsRobotTripTimeMs  { get; internal set; }

    /// <summary>Seconds since the robot was last linked. Updated each tick.</summary>
    public double SecondsSinceLastRobotLink { get; internal set; }

    internal DateTime LastPacketTime      { get; set; } = DateTime.MinValue;
    internal DateTime LastRobotLinkedTime { get; set; } = DateTime.MinValue;

    // ── Stops ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// E-stop — latched. Robot disabled and must be power-cycled to recover.
    /// Cleared only by ResetStops when no match is running.
    /// </summary>
    public bool Estop { get; set; }

    /// <summary>
    /// A-stop (autonomous stop) — disables this robot for the rest of auto.
    /// Cleared automatically when Teleop begins or by ResetStops.
    /// </summary>
    public bool Astop { get; set; }

    /// <summary>Bypass this slot — allows match to start without a DS connected here.</summary>
    public bool Bypassed { get; set; }

    // ── Packet sequence ────────────────────────────────────────────────────────

    /// <summary>Running count of control packets sent; used as the sequence number.</summary>
    internal int TxSequence { get; set; }

    // ── Derived ────────────────────────────────────────────────────────────────

    public bool IsLinked => DsLinked && RobotLinked;

    /// <summary>True when this station is ready for a match to start.</summary>
    public bool IsReady  => (Bypassed || IsLinked) && !Estop;

    public DriverStationConnection(AllianceStation station) => Station = station;
}
