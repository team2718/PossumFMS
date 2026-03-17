using MongoDB.Bson;
using PossumFMS.Core.Arena;
using PossumFMS.Core.FieldHardware;
using System.Net.Sockets;
using Xunit;

namespace PossumFMS.Core.Tests.FieldHardware;

public sealed class FieldHardwareProtocolTests : IDisposable
{
    private readonly FieldHardwareProtocol _protocol = new();
    private readonly TcpClient   _client;
    private readonly Socket      _serverSocket;
    private readonly FieldDevice _device;

    public FieldHardwareProtocolTests()
    {
        (_client, _serverSocket) = FakeTcpClientFactory.Create();
        _device = new FieldDevice(_client);
    }

    public void Dispose()
    {
        _serverSocket.Dispose();
        _client.Dispose();
    }

    // ── ParseHeartbeat — Hub ───────────────────────────────────────────────────

    [Fact]
    public void ParseHeartbeat_ValidHubRed_SetsTypeToHub()
    {
        var doc = new BsonDocument
        {
            { "name", "hub-red" },
            { "type", "hub" },
            { "alliance", "red" },
            { "fuel_delta", 3 },
        };

        _protocol.ParseHeartbeat(_device, doc);

        Assert.Equal(FieldDeviceType.Hub, _device.Type);
    }

    [Fact]
    public void ParseHeartbeat_ValidHubBlue_SetsHeartbeatAlliance()
    {
        var doc = new BsonDocument
        {
            { "name", "hub-blue" },
            { "type", "hub" },
            { "alliance", "blue" },
            { "fuel_delta", 0 },
        };

        _protocol.ParseHeartbeat(_device, doc);

        var hb = Assert.IsType<HubHeartbeat>(_device.LastHeartbeat);
        Assert.Equal("blue", hb.Alliance);
    }

    [Fact]
    public void ParseHeartbeat_Hub_StoresFuelDelta()
    {
        var doc = new BsonDocument
        {
            { "name", "hub-red" },
            { "type", "hub" },
            { "alliance", "red" },
            { "fuel_delta", 7 },
        };

        _protocol.ParseHeartbeat(_device, doc);

        var hb = Assert.IsType<HubHeartbeat>(_device.LastHeartbeat);
        Assert.Equal(7, hb.FuelDelta);
    }

    [Fact]
    public void ParseHeartbeat_Hub_MissingFuelDelta_DefaultsToZero()
    {
        var doc = new BsonDocument
        {
            { "name", "hub-red" },
            { "type", "hub" },
            { "alliance", "red" },
            // no fuel_delta field
        };

        _protocol.ParseHeartbeat(_device, doc);

        var hb = Assert.IsType<HubHeartbeat>(_device.LastHeartbeat);
        Assert.Equal(0, hb.FuelDelta);
    }

    [Fact]
    public void ParseHeartbeat_Hub_InvalidAlliance_Throws()
    {
        var doc = new BsonDocument
        {
            { "name", "hub-??" },
            { "type", "hub" },
            { "alliance", "green" }, // invalid
            { "fuel_delta", 0 },
        };

        Assert.Throws<InvalidOperationException>(() => _protocol.ParseHeartbeat(_device, doc));
    }

    [Fact]
    public void ParseHeartbeat_Hub_MissingAlliance_Throws()
    {
        var doc = new BsonDocument
        {
            { "name", "hub-??" },
            { "type", "hub" },
            { "fuel_delta", 0 },
        };

        Assert.Throws<InvalidOperationException>(() => _protocol.ParseHeartbeat(_device, doc));
    }

    // ── ParseHeartbeat — Estop ─────────────────────────────────────────────────

    [Fact]
    public void ParseHeartbeat_ValidEstop_SetsTypeToEstop()
    {
        var doc = new BsonDocument
        {
            { "name", "estop-1" },
            { "type", "estop" },
            { "astop_activated", false },
            { "estop_activated", false },
        };

        _protocol.ParseHeartbeat(_device, doc);

        Assert.Equal(FieldDeviceType.Estop, _device.Type);
    }

    [Fact]
    public void ParseHeartbeat_Estop_StoresActivatedFlagsTrue()
    {
        var doc = new BsonDocument
        {
            { "name", "estop-1" },
            { "type", "estop" },
            { "astop_activated", true },
            { "estop_activated", true },
        };

        _protocol.ParseHeartbeat(_device, doc);

        var hb = Assert.IsType<EstopHeartbeat>(_device.LastHeartbeat);
        Assert.True(hb.AstopActivated);
        Assert.True(hb.EstopActivated);
    }

    [Fact]
    public void ParseHeartbeat_Estop_MissingFlags_DefaultFalse()
    {
        var doc = new BsonDocument
        {
            { "name", "estop-1" },
            { "type", "estop" },
            // neither astop_activated nor estop_activated
        };

        _protocol.ParseHeartbeat(_device, doc);

        var hb = Assert.IsType<EstopHeartbeat>(_device.LastHeartbeat);
        Assert.False(hb.AstopActivated);
        Assert.False(hb.EstopActivated);
    }

    // ── ParseHeartbeat — error paths ───────────────────────────────────────────

    [Fact]
    public void ParseHeartbeat_MissingTypeField_Throws()
    {
        var doc = new BsonDocument { { "name", "device-1" } };

        Assert.Throws<InvalidOperationException>(() => _protocol.ParseHeartbeat(_device, doc));
    }

    [Fact]
    public void ParseHeartbeat_EmptyTypeField_Throws()
    {
        var doc = new BsonDocument { { "name", "device-1" }, { "type", "" } };

        Assert.Throws<InvalidOperationException>(() => _protocol.ParseHeartbeat(_device, doc));
    }

    [Fact]
    public void ParseHeartbeat_UnknownType_Throws()
    {
        var doc = new BsonDocument
        {
            { "name", "laser" },
            { "type", "laser_cannon" },
        };

        Assert.Throws<InvalidOperationException>(() => _protocol.ParseHeartbeat(_device, doc));
    }

    // ── BuildReply — parse error ───────────────────────────────────────────────

    [Fact]
    public void BuildReply_WithParseError_ReturnsAcceptedFalse()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        var logic = new GameLogic(arena);

        var reply = _protocol.BuildReply(_device, arena, logic, "some error");

        Assert.False(reply["accepted"].AsBoolean);
        Assert.Equal("some error", reply["error"].AsString);
    }

    // ── BuildReply — Hub LED colors ────────────────────────────────────────────

    private FieldDevice MakeHubDevice(string alliance)
    {
        var doc = new BsonDocument
        {
            { "name", $"hub-{alliance}" },
            { "type", "hub" },
            { "alliance", alliance },
            { "fuel_delta", 0 },
        };
        _protocol.ParseHeartbeat(_device, doc);
        return _device;
    }

    [Fact]
    public void BuildReply_Hub_WhenIdle_LedIsGreen()
    {
        var arena = new PossumFMS.Core.Arena.Arena(); // Idle
        var logic = new GameLogic(arena);
        MakeHubDevice("red");

        var reply = _protocol.BuildReply(_device, arena, logic, null);

        Assert.Equal(0,   reply["led_r"].AsInt32);
        Assert.Equal(255, reply["led_g"].AsInt32);
        Assert.Equal(0,   reply["led_b"].AsInt32);
    }

    [Fact]
    public void BuildReply_Hub_WhenPostMatch_LedIsPurple()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();
        arena.AbortMatch(); // PostMatch
        var logic = new GameLogic(arena);
        MakeHubDevice("red");

        var reply = _protocol.BuildReply(_device, arena, logic, null);

        Assert.Equal(128, reply["led_r"].AsInt32);
        Assert.Equal(0,   reply["led_g"].AsInt32);
        Assert.Equal(128, reply["led_b"].AsInt32);
    }

    [Fact]
    public void BuildReply_Hub_WhenAutoAndRedHub_LedIsRed()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto — both hubs active
        var logic = new GameLogic(arena);
        MakeHubDevice("red");

        var reply = _protocol.BuildReply(_device, arena, logic, null);

        Assert.Equal(255, reply["led_r"].AsInt32);
        Assert.Equal(0,   reply["led_g"].AsInt32);
        Assert.Equal(0,   reply["led_b"].AsInt32);
    }

    [Fact]
    public void BuildReply_Hub_WhenAutoAndBlueHub_LedIsBlue()
    {
        var (client2, server2) = FakeTcpClientFactory.Create();
        try
        {
            var blueDevice = new FieldDevice(client2);
            var arena = new PossumFMS.Core.Arena.Arena();
            arena.StartPreMatch();
            arena.StartMatch();
            var logic = new GameLogic(arena);

            var doc = new BsonDocument
            {
                { "name", "hub-blue" },
                { "type", "hub" },
                { "alliance", "blue" },
                { "fuel_delta", 0 },
            };
            _protocol.ParseHeartbeat(blueDevice, doc);

            var reply = _protocol.BuildReply(blueDevice, arena, logic, null);

            Assert.Equal(0,   reply["led_r"].AsInt32);
            Assert.Equal(0,   reply["led_g"].AsInt32);
            Assert.Equal(255, reply["led_b"].AsInt32);
        }
        finally
        {
            server2.Dispose();
            client2.Dispose();
        }
    }

    [Fact]
    public void BuildReply_Hub_WhenHubInactive_LedIsOff()
    {
        // Hub is inactive when match is not in progress (e.g. PreMatch)
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        var logic = new GameLogic(arena);
        MakeHubDevice("red");

        var reply = _protocol.BuildReply(_device, arena, logic, null);

        Assert.Equal(0, reply["led_r"].AsInt32);
        Assert.Equal(0, reply["led_g"].AsInt32);
        Assert.Equal(0, reply["led_b"].AsInt32);
    }

    // ── BuildReply — flashing_status ──────────────────────────────────────────

    [Fact]
    public void BuildReply_Hub_WhenIdle_FlashingStatusIsOff()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        var logic = new GameLogic(arena);
        MakeHubDevice("red");

        var reply = _protocol.BuildReply(_device, arena, logic, null);

        Assert.Equal("off", reply["flashing_status"].AsString);
    }

    [Fact]
    public void BuildReply_Hub_WhenAutoActive_FlashingStatusIsOff()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();
        var logic = new GameLogic(arena);
        MakeHubDevice("red");

        var reply = _protocol.BuildReply(_device, arena, logic, null);

        Assert.Equal("off", reply["flashing_status"].AsString);
    }

    // ── BuildReply — Estop device ──────────────────────────────────────────────

    [Fact]
    public void BuildReply_EstopDevice_ReturnsAcceptedTrue()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        var logic = new GameLogic(arena);

        var doc = new BsonDocument
        {
            { "name", "estop-1" },
            { "type", "estop" },
            { "astop_activated", false },
            { "estop_activated", false },
        };
        _protocol.ParseHeartbeat(_device, doc);

        var reply = _protocol.BuildReply(_device, arena, logic, null);

        Assert.True(reply["accepted"].AsBoolean);
    }

    // ── BuildReply — unknown device type ──────────────────────────────────────

    [Fact]
    public void BuildReply_UnknownDevice_ReturnsAcceptedTrueWithMessage()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        var logic = new GameLogic(arena);
        // _device.Type is still Unknown (no heartbeat applied)

        var reply = _protocol.BuildReply(_device, arena, logic, null);

        Assert.True(reply["accepted"].AsBoolean);
        Assert.True(reply.Contains("message"));
    }
}
