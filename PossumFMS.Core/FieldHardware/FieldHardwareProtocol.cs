using MongoDB.Bson;
using PossumFMS.Core.Arena;

namespace PossumFMS.Core.FieldHardware;

public sealed class FieldHardwareProtocol
{
    private readonly Dictionary<FieldDeviceType, IFieldDeviceProtocolHandler> _handlersByType;

    // Protocol handlers go here
    public FieldHardwareProtocol()
        : this([new HubDeviceProtocolHandler(), new EstopDeviceProtocolHandler()])
    {
    }

    // Use each protocol's DeviceType property to build the lookup dictionary
    internal FieldHardwareProtocol(IEnumerable<IFieldDeviceProtocolHandler> handlers)
    {
        _handlersByType = handlers
            .GroupBy(h => h.DeviceType)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public bool ParseHeartbeat(FieldDevice device, BsonDocument heartbeat)
    {
        var name = BsonField.GetString(heartbeat, "name");

        var type = BsonField.GetString(heartbeat, "type");
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidOperationException("Heartbeat must include a 'type' field.");

        device.UpdateIdentity(name, type);

        if (!_handlersByType.TryGetValue(device.Type, out var handler))
            throw new InvalidOperationException($"Unsupported device type '{type}'.");

        var parseResult = handler.Parse(heartbeat);
        device.ApplyHeartbeat(parseResult.Heartbeat);

        return parseResult.TriggerArenaEstop;
    }

    public BsonDocument BuildReply(FieldDevice device, Arena.Arena arena, GameLogic gameLogic, string? parseError)
    {
        if (parseError is not null)
        {
            return new BsonDocument
            {
                { "accepted", false },
                { "error", parseError }
            };
        }

        if (!_handlersByType.TryGetValue(device.Type, out var handler))
        {
            return new BsonDocument
            {
                { "accepted", true },
                { "message", "Unknown device type; no command payload." }
            };
        }

        return handler.BuildReply(device, arena, gameLogic);
    }
}

internal interface IFieldDeviceProtocolHandler
{
    FieldDeviceType DeviceType { get; }
    DeviceHeartbeatParseResult Parse(BsonDocument heartbeat);
    BsonDocument BuildReply(FieldDevice device, Arena.Arena arena, GameLogic gameLogic);
}

internal readonly record struct DeviceHeartbeatParseResult(FieldDeviceHeartbeat Heartbeat, bool TriggerArenaEstop = false);

internal sealed class HubDeviceProtocolHandler : IFieldDeviceProtocolHandler
{
    public FieldDeviceType DeviceType => FieldDeviceType.Hub;

    public DeviceHeartbeatParseResult Parse(BsonDocument heartbeat)
    {
        var alliance = BsonField.GetString(heartbeat, "alliance")?.ToLowerInvariant();
        var fuelDelta = BsonField.GetInt32(heartbeat, "fuel_delta") ?? 0;

        if (alliance is not ("red" or "blue"))
            throw new InvalidOperationException("Hub heartbeat must include alliance as 'red' or 'blue'.");

        return new DeviceHeartbeatParseResult(new HubHeartbeat(alliance, fuelDelta, DateTime.UtcNow));
    }

    public BsonDocument BuildReply(FieldDevice device, Arena.Arena arena, GameLogic gameLogic)
    {
        var hubHeartbeat = device.LastHeartbeat as HubHeartbeat;
        var alliance = ParseAlliance(hubHeartbeat?.Alliance);
        var (r, g, b) = GetHubLedColor(alliance, arena, gameLogic);
        var flashingStatus = GetFlashingStatus(alliance, arena, gameLogic);

        return new BsonDocument
        {
            { "flashing_status", flashingStatus },
            { "led_r", r},
            { "led_g", g},
            { "led_b", b}
        };
    }

    private static (int r, int g, int b) GetHubLedColor(AllianceColor? alliance, Arena.Arena arena, GameLogic gameLogic)
    {
        if (arena.Phase == MatchPhase.Idle)
            return (0, 255, 0); // Green

        if (arena.Phase == MatchPhase.PostMatch)
            return (128, 0, 128); // Purple

        if (arena.IsMatchRunning && alliance is not null && gameLogic.IsHubStrictlyActive(alliance.Value))
        {
            return alliance == AllianceColor.Red
                ? (255, 0, 0)
                : (0, 0, 255);
        }

        return (0, 0, 0); // Off
    }

    private static string GetFlashingStatus(AllianceColor? alliance, Arena.Arena arena, GameLogic gameLogic)
    {
        if (alliance is null)
            return "off";

        if (arena.Phase == MatchPhase.Teleop
            && gameLogic.CurrentTeleopPeriod == TeleopPeriod.TransitionShift
            && gameLogic.ShiftAutoWinnerAlliance == alliance)
        {
            return "flash_white";
        }

        if (gameLogic.IsHubAboutToBecomeInactive(alliance.Value, TimeSpan.FromSeconds(3)))
            return "flash_off";

        return "off";
    }

    private static AllianceColor? ParseAlliance(string? alliance)
    {
        if (string.Equals(alliance, "red", StringComparison.OrdinalIgnoreCase))
            return AllianceColor.Red;

        if (string.Equals(alliance, "blue", StringComparison.OrdinalIgnoreCase))
            return AllianceColor.Blue;

        return null;
    }
}

internal sealed class EstopDeviceProtocolHandler : IFieldDeviceProtocolHandler
{
    public FieldDeviceType DeviceType => FieldDeviceType.Estop;

    public DeviceHeartbeatParseResult Parse(BsonDocument heartbeat)
    {
        var astop = BsonField.GetBoolean(heartbeat, "astop_activated")
            ?? false;

        var estop = BsonField.GetBoolean(heartbeat, "estop_activated")
            ?? false;

        return new DeviceHeartbeatParseResult(new EstopHeartbeat(astop, estop, DateTime.UtcNow), estop);
    }

    public BsonDocument BuildReply(FieldDevice device, Arena.Arena arena, GameLogic gameLogic)
    {
        _ = device;
        _ = arena;
        _ = gameLogic;
        return new BsonDocument();
    }
}

internal static class BsonField
{
    public static string? GetString(BsonDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out var value) || value.IsBsonNull)
            return null;

        return value.IsString ? value.AsString : value.ToString();
    }

    public static int? GetInt32(BsonDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out var value) || value.IsBsonNull)
            return null;

        return value.IsInt32 ? value.AsInt32 : value.ToInt32();
    }

    public static bool? GetBoolean(BsonDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out var value) || value.IsBsonNull)
            return null;

        return value.IsBoolean ? value.AsBoolean : value.ToBoolean();
    }
}
