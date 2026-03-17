using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using PossumFMS.Core.Arena;

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
    private readonly ILogger<FieldHardwareManager> _logger;
    private readonly Arena.Arena _arena;
    private readonly FieldHardwareProtocol _protocol = new();
    private readonly int _listenPort;
    private readonly IPAddress _listenAddress;
    private readonly TimeSpan _clientTimeout;

    private TcpListener? _listener;
    private int _nextDeviceId;

    public IReadOnlyList<FieldDevice> Devices => _devices.Values.ToList();

    public FieldHardwareManager(IConfiguration config, Arena.Arena arena, ILogger<FieldHardwareManager> logger)
    {
        _logger = logger;
        _arena = arena;

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

        try
        {
            await using var stream = device.Stream;

            while (!serviceCt.IsCancellationRequested)
            {
                var heartbeat = await ReadBsonDocumentAsync(stream, serviceCt);
                if (heartbeat is null)
                    break;

                string? parseError = null;
                try
                {
                    if (_protocol.ParseHeartbeat(device, heartbeat))
                        _arena.TriggerArenaEstop();
                }
                catch (Exception ex)
                {
                    parseError = ex.Message;
                    _logger.LogWarning("Invalid field hardware packet from {RemoteEndpoint}: {Error}", endpoint, ex.Message);
                }

                device.LastSeen = DateTime.UtcNow;
                var response = _protocol.BuildReply(device, _arena, parseError);
                await stream.WriteAsync(response.ToBson(), serviceCt);
            }
        }
        catch (TimeoutException)
        {
            if (device.Type == FieldDeviceType.Estop)
                AbortMatchForEstopTimeout(device, endpoint);

            _logger.LogWarning("Field device {Name} at {RemoteEndpoint} timed out after {TimeoutMs}ms.",
                device.Name, endpoint, _clientTimeout.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (serviceCt.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Field device {Name} at {RemoteEndpoint} disconnected with error: {Error}",
                device.Name, endpoint, ex.Message);
        }
        finally
        {
            device.Status = FieldDeviceStatus.Disconnected;
            _devices.TryRemove(deviceId, out _);
            client.Dispose();

            _logger.LogInformation("Field device {Name} at {RemoteEndpoint} disconnected.", device.Name, endpoint);
        }
    }

    private void AbortMatchForEstopTimeout(FieldDevice device, string endpoint)
    {
        if (!_arena.IsMatchRunning)
            return;

        try
        {
            _arena.AbortMatch();
            _logger.LogError(
                "Estop device {Name} at {RemoteEndpoint} timed out; match aborted immediately.",
                device.Name,
                endpoint);
        }
        catch (InvalidOperationException)
        {
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
