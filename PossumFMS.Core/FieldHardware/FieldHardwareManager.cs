using System.Net.Sockets;
using System.Text;

namespace PossumFMS.Core.FieldHardware;

/// <summary>
/// Manages TCP connections to all ESP32 field devices on the local network.
/// Reconnects automatically when a device drops, and provides a simple
/// send/receive interface for field hardware commands.
///
/// Device addresses follow the convention defined in appsettings.json under
/// the "FieldHardware:Devices" section.
/// </summary>
public sealed class FieldHardwareManager : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval   = TimeSpan.FromMilliseconds(50);

    private readonly List<FieldDevice> _devices;
    private readonly ILogger<FieldHardwareManager> _logger;

    public IReadOnlyList<FieldDevice> Devices => _devices;

    public FieldHardwareManager(IConfiguration config, ILogger<FieldHardwareManager> logger)
    {
        _logger = logger;

        // Load device list from config. Example appsettings.json section:
        //
        //   "FieldHardware": {
        //     "Devices": [
        //       { "Name": "RedScorer",  "Host": "10.0.100.11", "Port": 8080 },
        //       { "Name": "BlueScorer", "Host": "10.0.100.12", "Port": 8080 }
        //     ]
        //   }

        _devices = config.GetSection("FieldHardware:Devices")
            .Get<List<FieldDeviceConfig>>()
            ?.Select(c => new FieldDevice(c.Name, c.Host, c.Port))
            .ToList()
            ?? [];

        if (_devices.Count == 0)
            _logger.LogWarning("No field hardware devices configured. Add entries to FieldHardware:Devices in appsettings.json.");
        else
            _logger.LogInformation("Loaded {Count} field hardware device(s).", _devices.Count);
    }

    // ── BackgroundService ──────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Launch a dedicated connection task per device.
        var tasks = _devices.Select(d => ManageDeviceAsync(d, ct));
        await Task.WhenAll(tasks);
    }

    private async Task ManageDeviceAsync(FieldDevice device, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to field device {Name} at {Host}:{Port}…",
                    device.Name, device.Host, device.Port);

                device.Client = new TcpClient();
                await device.Client.ConnectAsync(device.Host, device.Port, ct);

                device.Status   = FieldDeviceStatus.Connected;
                device.LastSeen = DateTime.UtcNow;

                _logger.LogInformation("Field device {Name} connected.", device.Name);

                await DeviceSessionAsync(device, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Field device {Name} error: {Error}. Retrying in {Delay}s.",
                    device.Name, ex.Message, ReconnectDelay.TotalSeconds);
            }
            finally
            {
                device.Client?.Dispose();
                device.Client  = null;
                device.Status  = FieldDeviceStatus.Disconnected;
            }

            await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false);
        }
    }

    private async Task DeviceSessionAsync(FieldDevice device, CancellationToken ct)
    {
        var stream = device.Stream!;
        var rxBuf  = new byte[256];

        while (!ct.IsCancellationRequested && device.Client?.Connected == true)
        {
            // ── Receive any pending data ───────────────────────────────────────
            if (stream.DataAvailable)
            {
                int bytes = await stream.ReadAsync(rxBuf, ct);
                if (bytes == 0) break;  // Connection closed by remote.

                device.LastSeen = DateTime.UtcNow;
                ParseDevicePacket(device, rxBuf.AsSpan(0, bytes));
            }

            // TODO: Send periodic heartbeat / field command packets here.

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    private static void ParseDevicePacket(FieldDevice device, ReadOnlySpan<byte> data)
    {
        // TODO: Decode ESP32 field hardware protocol.
        _ = device;
        _ = data;
    }

    // ── Send helpers ───────────────────────────────────────────────────────────

    /// <summary>Sends raw bytes to a specific field device. No-op if not connected.</summary>
    public async Task SendAsync(FieldDevice device, byte[] data, CancellationToken ct = default)
    {
        if (!device.IsConnected || device.Stream is null) return;

        try
        {
            await device.Stream.WriteAsync(data, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Send to {Name} failed: {Error}", device.Name, ex.Message);
            device.Status = FieldDeviceStatus.Error;
        }
    }

    // ── Config binding ─────────────────────────────────────────────────────────

    private sealed class FieldDeviceConfig
    {
        public string Name { get; init; } = string.Empty;
        public string Host { get; init; } = string.Empty;
        public int    Port { get; init; } = 8080;
    }
}
