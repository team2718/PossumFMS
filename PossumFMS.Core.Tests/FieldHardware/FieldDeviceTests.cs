using System.Net.Sockets;
using PossumFMS.Core.FieldHardware;
using Xunit;

namespace PossumFMS.Core.Tests.FieldHardware;

/// <summary>
/// Creates a loopback TcpClient pair so FieldDevice can be constructed without a
/// real network. The server-side socket is stored to keep the connection alive.
/// </summary>
internal static class FakeTcpClientFactory
{
    public static (TcpClient Client, Socket ServerSocket) Create()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var clientTask = Task.Run(() => new TcpClient("127.0.0.1", port));
        var serverSock = listener.AcceptSocket();
        listener.Stop();

        return (clientTask.GetAwaiter().GetResult(), serverSock);
    }
}

public sealed class FieldDeviceTests : IDisposable
{
    private readonly TcpClient   _client;
    private readonly Socket      _serverSocket;
    private readonly FieldDevice _device;

    public FieldDeviceTests()
    {
        (_client, _serverSocket) = FakeTcpClientFactory.Create();
        _device = new FieldDevice(_client);
    }

    public void Dispose()
    {
        _serverSocket.Dispose();
        _client.Dispose();
    }

    // ── Default state ──────────────────────────────────────────────────────────

    [Fact]
    public void DefaultName_IsUnknown()
    {
        Assert.Equal("unknown", _device.Name);
    }

    [Fact]
    public void DefaultType_IsUnknown()
    {
        Assert.Equal(FieldDeviceType.Unknown, _device.Type);
    }

    [Fact]
    public void DefaultStatus_IsConnected()
    {
        Assert.Equal(FieldDeviceStatus.Connected, _device.Status);
    }

    [Fact]
    public void DefaultLastHeartbeat_IsNull()
    {
        Assert.Null(_device.LastHeartbeat);
    }

    [Fact]
    public void IsConnected_WhenStatusConnectedAndTcpAlive_True()
    {
        Assert.True(_device.IsConnected);
    }

    [Fact]
    public void IsConnected_WhenStatusDisconnected_False()
    {
        _device.Status = FieldDeviceStatus.Disconnected;

        Assert.False(_device.IsConnected);
    }

    [Fact]
    public void IsConnected_WhenStatusError_False()
    {
        _device.Status = FieldDeviceStatus.Error;

        Assert.False(_device.IsConnected);
    }

    // ── UpdateIdentity ─────────────────────────────────────────────────────────

    [Fact]
    public void UpdateIdentity_SetsNameFromNonEmptyString()
    {
        _device.UpdateIdentity("hub-red", "hub");

        Assert.Equal("hub-red", _device.Name);
    }

    [Fact]
    public void UpdateIdentity_NullName_DoesNotChangeName()
    {
        _device.UpdateIdentity("original", "hub");
        _device.UpdateIdentity(null, "hub");

        Assert.Equal("original", _device.Name);
    }

    [Fact]
    public void UpdateIdentity_WhitespaceName_DoesNotChangeName()
    {
        _device.UpdateIdentity("original", "hub");
        _device.UpdateIdentity("   ", "hub");

        Assert.Equal("original", _device.Name);
    }

    [Fact]
    public void UpdateIdentity_TrimsWhitespaceFromName()
    {
        _device.UpdateIdentity("  blue-hub  ", "hub");

        Assert.Equal("blue-hub", _device.Name);
    }

    [Theory]
    [InlineData("hub",   FieldDeviceType.Hub)]
    [InlineData("Hub",   FieldDeviceType.Hub)]
    [InlineData("HUB",   FieldDeviceType.Hub)]
    [InlineData("estop", FieldDeviceType.Estop)]
    [InlineData("Estop", FieldDeviceType.Estop)]
    [InlineData("ESTOP", FieldDeviceType.Estop)]
    public void UpdateIdentity_ParsesKnownTypesCaseInsensitively(string raw, FieldDeviceType expected)
    {
        _device.UpdateIdentity("dev", raw);

        Assert.Equal(expected, _device.Type);
    }

    [Fact]
    public void UpdateIdentity_NullType_SetsUnknown()
    {
        _device.UpdateIdentity("dev", null);

        Assert.Equal(FieldDeviceType.Unknown, _device.Type);
    }

    [Fact]
    public void UpdateIdentity_EmptyType_SetsUnknown()
    {
        _device.UpdateIdentity("dev", "");

        Assert.Equal(FieldDeviceType.Unknown, _device.Type);
    }

    [Fact]
    public void UpdateIdentity_UnrecognisedType_SetsUnknown()
    {
        _device.UpdateIdentity("dev", "laser_cannon");

        Assert.Equal(FieldDeviceType.Unknown, _device.Type);
    }

    // ── ApplyHeartbeat ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyHeartbeat_Hub_SetsTypeToHub()
    {
        var heartbeat = new HubHeartbeat("red", 5, DateTime.UtcNow);

        _device.ApplyHeartbeat(heartbeat);

        Assert.Equal(FieldDeviceType.Hub, _device.Type);
    }

    [Fact]
    public void ApplyHeartbeat_Estop_SetsTypeToEstop()
    {
        var heartbeat = new EstopHeartbeat("red", 1, false, false, DateTime.UtcNow);

        _device.ApplyHeartbeat(heartbeat);

        Assert.Equal(FieldDeviceType.Estop, _device.Type);
    }

    [Fact]
    public void ApplyHeartbeat_StoresHeartbeat()
    {
        var heartbeat = new HubHeartbeat("blue", 3, DateTime.UtcNow);

        _device.ApplyHeartbeat(heartbeat);

        Assert.Same(heartbeat, _device.LastHeartbeat);
    }

    [Fact]
    public void ApplyHeartbeat_ReplacesLastHeartbeat()
    {
        var first  = new HubHeartbeat("red",  1, DateTime.UtcNow);
        var second = new HubHeartbeat("blue", 2, DateTime.UtcNow);

        _device.ApplyHeartbeat(first);
        _device.ApplyHeartbeat(second);

        Assert.Same(second, _device.LastHeartbeat);
    }

    // ── HubHeartbeat record ────────────────────────────────────────────────────

    [Fact]
    public void HubHeartbeat_StoresAllProperties()
    {
        var now = DateTime.UtcNow;
        var hb  = new HubHeartbeat("red", 10, now);

        Assert.Equal("red", hb.Alliance);
        Assert.Equal(10, hb.FuelCount);
        Assert.Equal(now, hb.ReceivedUtc);
    }

    // ── EstopHeartbeat record ──────────────────────────────────────────────────

    [Fact]
    public void EstopHeartbeat_StoresAllProperties()
    {
        var now = DateTime.UtcNow;
        var hb  = new EstopHeartbeat("red", 1, true, false, now);

        Assert.True(hb.AstopActivated);
        Assert.False(hb.EstopActivated);
        Assert.Equal(now, hb.ReceivedUtc);
    }

    // ── RemoteEndpoint ─────────────────────────────────────────────────────────

    [Fact]
    public void RemoteEndpoint_IsNotNullWhenConnected()
    {
        Assert.NotNull(_device.RemoteEndpoint);
    }

    // ── LastSeen ───────────────────────────────────────────────────────────────

    [Fact]
    public void LastSeen_SetOnConstruction_IsRecentUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var after  = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(_device.LastSeen, before, after);
    }
}
