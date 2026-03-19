using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;

namespace PossumFMS.Core.FieldHardware;

/// <summary>
/// Hosts the field hardware TCP server used by ESP32 clients.
/// Devices connect and send BSON heartbeat packets roughly every 20ms.
/// </summary>
public sealed class FieldHardwareManager : BackgroundService
{
    private static readonly TimeSpan DefaultClientTimeout = TimeSpan.FromMilliseconds(500);
    private const int DefaultListenPort = 1678;
    private const int MaxBsonPacketSizeBytes = 1024 * 1024;

    private readonly ConcurrentDictionary<int, FieldDevice> _devices = new();
    private readonly Lock _hubHeartbeatDeduplicationLock = new();
    private readonly Dictionary<string, int> _lastAcceptedHubHeartbeatIdsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FieldHardwareManager> _logger;
    private readonly Arena.Arena _arena;
    private readonly GameLogic _gameLogic;
    private readonly DriverStationManager _driverStationManager;
    private readonly FieldHardwareProtocol _protocol = new();
    private readonly int _listenPort;
    private readonly IPAddress _listenAddress;
    private readonly TimeSpan _clientTimeout;

    private TcpListener? _listener;
    private int _nextDeviceId;

    public IReadOnlyList<FieldDevice> Devices => _devices.Values.ToList();

    public FieldHardwareManager(
        IConfiguration config,
        Arena.Arena arena,
        GameLogic gameLogic,
        DriverStationManager driverStationManager,
        ILogger<FieldHardwareManager> logger)
    {
        _logger = logger;
        _arena = arena;
        _gameLogic = gameLogic;
        _driverStationManager = driverStationManager;

        var section = config.GetSection("FieldHardware");
        _listenPort = section.GetValue<int?>("ListenPort") ?? DefaultListenPort;

        var timeoutMs = section.GetValue<int?>("ClientTimeoutMs")
            ?? (int)DefaultClientTimeout.TotalMilliseconds;
        _clientTimeout = TimeSpan.FromMilliseconds(Math.Max(100, timeoutMs));

        var configuredAddress = section.GetValue<string>("ListenAddress");
        _listenAddress = string.IsNullOrWhiteSpace(configuredAddress)
            ? IPAddress.Any
            : IPAddress.Parse(configuredAddress);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _listener = new TcpListener(_listenAddress, _listenPort);
        _listener.Start();

        _logger.LogInformation(
            "Field hardware TCP server listening on {Address}:{Port} with timeout {TimeoutMs}ms.",
            _listenAddress,
            _listenPort,
            _clientTimeout.TotalMilliseconds);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }

                _ = HandleClientAsync(client, ct);
            }
        }
        finally
        {
            _listener.Stop();

            foreach (var kv in _devices)
                kv.Value.Client.Dispose();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serviceCt)
    {
        var deviceId = Interlocked.Increment(ref _nextDeviceId);
        var device = new FieldDevice(client);
        _devices[deviceId] = device;

        var endpoint = device.RemoteEndpoint?.ToString() ?? "unknown";
        _logger.LogInformation("Field device connected from {RemoteEndpoint}.", endpoint);

        client.NoDelay = true;
        bool remoteClosedConnection = false;

        try
        {
            await using var stream = device.Stream;

            while (!serviceCt.IsCancellationRequested)
            {
                var heartbeat = await ReadBsonDocumentAsync(stream, serviceCt);
                if (heartbeat is null)
                {
                    remoteClosedConnection = true;
                    break;
                }

                string? parseError = null;
                try
                {
                    _protocol.ParseHeartbeat(device, heartbeat);

                    if (device.LastHeartbeat is EstopHeartbeat estopHeartbeat)
                    {
                        if (estopHeartbeat.EstopActivated) {
                            _logger.LogWarning("E-Stop triggered by {DeviceName} for {Alliance} {Station}", device.Name, estopHeartbeat.Alliance, estopHeartbeat.Station);
                            
                            if (estopHeartbeat.Alliance == "field")
                            {
                                _arena.TriggerArenaEstop();
                            }
                            else if (estopHeartbeat.Station == 0)
                            {
                                // If station is 0, treat it as an estop for all stations on that alliance.
                                var alliance = estopHeartbeat.Alliance == "red" ? AllianceColor.Red : AllianceColor.Blue;
                                _driverStationManager.Estop(new AllianceStation(alliance, StationPosition.One));
                                _driverStationManager.Estop(new AllianceStation(alliance, StationPosition.Two));
                                _driverStationManager.Estop(new AllianceStation(alliance, StationPosition.Three));
                            }
                            else
                            {
                                var station = new AllianceStation(
                                    estopHeartbeat.Alliance == "red" ? AllianceColor.Red : AllianceColor.Blue,
                                    (StationPosition)estopHeartbeat.Station);

                                _driverStationManager.Estop(station);
                            }
                        }

                        if (estopHeartbeat.AstopActivated) {
                            _logger.LogWarning("A-Stop triggered by {DeviceName} for {Alliance} {Station}", device.Name, estopHeartbeat.Alliance, estopHeartbeat.Station);
                            
                            if (estopHeartbeat.Alliance == "field")
                            {
                                _driverStationManager.AstopAll();
                            }
                            else if (estopHeartbeat.Station == 0)
                            {
                                // If station is 0, treat it as an astop for all stations on that alliance.
                                var alliance = estopHeartbeat.Alliance == "red" ? AllianceColor.Red : AllianceColor.Blue;
                                _driverStationManager.Astop(new AllianceStation(alliance, StationPosition.One));
                                _driverStationManager.Astop(new AllianceStation(alliance, StationPosition.Two));
                                _driverStationManager.Astop(new AllianceStation(alliance, StationPosition.Three));
                            }
                            else
                            {
                                var station = new AllianceStation(
                                    estopHeartbeat.Alliance == "red" ? AllianceColor.Red : AllianceColor.Blue,
                                    (StationPosition)estopHeartbeat.Station);

                                _driverStationManager.Astop(station);
                            }
                        }
                    }

                    if (device.LastHeartbeat is HubHeartbeat hub)
                    {
                        if (hub.FuelDelta > 0 && TryAcceptHubHeartbeat(device.Name, hub.HeartbeatId))
                        {
                            var alliance = string.Equals(hub.Alliance, "red", StringComparison.OrdinalIgnoreCase)
                                ? AllianceColor.Red
                                : AllianceColor.Blue;
                            _gameLogic.ScoreFuel(alliance, hub.FuelDelta);
                        }
                    }
                }
                catch (Exception ex)
                {
                    parseError = ex.Message;
                    _logger.LogWarning("Invalid field hardware packet from {RemoteEndpoint}: {Error}", endpoint, ex.Message);
                }

                device.LastSeen = DateTime.UtcNow;
                var response = _protocol.BuildReply(device, _arena, _gameLogic, parseError);
                await stream.WriteAsync(response.ToBson(), serviceCt);
            }
        }
        catch (TimeoutException)
        {
            AbortMatchForCriticalDeviceDisconnect(
                device,
                endpoint,
                $"timed out after {_clientTimeout.TotalMilliseconds}ms");
        }
        catch (OperationCanceledException) when (serviceCt.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AbortMatchForCriticalDeviceDisconnect(
                device,
                endpoint,
                $"disconnected with error: {ex.Message}");
        }
        finally
        {
            if (remoteClosedConnection && !serviceCt.IsCancellationRequested)
            {
                AbortMatchForCriticalDeviceDisconnect(
                    device,
                    endpoint,
                    "closed the connection");
            }
        }

        device.Status = FieldDeviceStatus.Disconnected;
        _devices.TryRemove(deviceId, out _);
        client.Dispose();

        _logger.LogInformation("Field device {Name} at {RemoteEndpoint} disconnected.", device.Name, endpoint);
    }

    private void AbortMatchForCriticalDeviceDisconnect(FieldDevice device, string endpoint, string reason)
    {
        if (device.Type is not (FieldDeviceType.Estop or FieldDeviceType.Hub))
            return;

        _logger.LogWarning(
            "Field device {Name} ({Type}) at {RemoteEndpoint} {Reason}.",
            device.Name,
            device.Type,
            endpoint,
            reason);

        if (!_arena.IsMatchInProgress)
            return;

        try
        {
            _arena.AbortMatch();
            _logger.LogWarning(
                "Field device {Name} ({Type}) at {RemoteEndpoint} {Reason}; match aborted without asserting e-stop.",
                device.Name,
                device.Type,
                endpoint,
                reason);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool TryAcceptHubHeartbeat(string deviceName, int heartbeatId)
    {
        lock (_hubHeartbeatDeduplicationLock)
        {
            if (_lastAcceptedHubHeartbeatIdsByName.TryGetValue(deviceName, out var lastAcceptedHeartbeatId)
                && lastAcceptedHeartbeatId == heartbeatId)
            {
                return false;
            }

            _lastAcceptedHubHeartbeatIdsByName[deviceName] = heartbeatId;
            return true;
        }
    }

    private async Task<BsonDocument?> ReadBsonDocumentAsync(NetworkStream stream, CancellationToken serviceCt)
    {
        var lengthPrefix = new byte[sizeof(int)];
        var prefixRead = await ReadLengthPrefixAsync(stream, lengthPrefix, _clientTimeout, serviceCt);
        if (prefixRead == 0)
            return null;

        var documentLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (documentLength < 5 || documentLength > MaxBsonPacketSizeBytes)
            throw new InvalidOperationException($"Invalid BSON length {documentLength}.");

        var payload = new byte[documentLength];
        lengthPrefix.CopyTo(payload, 0);

        var remaining = documentLength - lengthPrefix.Length;
        if (remaining > 0)
            await ReadExactlyAsync(stream, payload.AsMemory(lengthPrefix.Length, remaining), _clientTimeout, serviceCt);

        return BsonSerializer.Deserialize<BsonDocument>(payload);
    }

    private static async Task<int> ReadLengthPrefixAsync(
        NetworkStream stream,
        byte[] lengthPrefix,
        TimeSpan timeout,
        CancellationToken serviceCt)
    {
        var totalRead = 0;

        while (totalRead < lengthPrefix.Length)
        {
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(serviceCt);
            readTimeout.CancelAfter(timeout);

            int read;
            try
            {
                read = await stream.ReadAsync(lengthPrefix.AsMemory(totalRead), readTimeout.Token);
            }
            catch (OperationCanceledException) when (!serviceCt.IsCancellationRequested)
            {
                throw new TimeoutException($"No message received for {timeout.TotalMilliseconds}ms.");
            }

            if (read == 0)
            {
                if (totalRead == 0)
                    return 0;

                throw new EndOfStreamException("Connection closed while receiving BSON length prefix.");
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        Memory<byte> buffer,
        TimeSpan timeout,
        CancellationToken serviceCt)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(serviceCt);
            readTimeout.CancelAfter(timeout);

            int read;
            try
            {
                read = await stream.ReadAsync(buffer[totalRead..], readTimeout.Token);
            }
            catch (OperationCanceledException) when (!serviceCt.IsCancellationRequested)
            {
                throw new TimeoutException($"No message received for {timeout.TotalMilliseconds}ms.");
            }

            if (read == 0)
                throw new EndOfStreamException("Connection closed while receiving BSON payload.");

            totalRead += read;
        }
    }

}
