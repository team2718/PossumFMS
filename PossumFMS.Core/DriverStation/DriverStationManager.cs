using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PossumFMS.Core.Arena;

namespace PossumFMS.Core.DriverStation;

/// <summary>
/// Manages all six driver station connections and runs the high-frequency
/// FMS control loop.
///
/// TIMING: FRC requires FMS→DS packets at ≤10 ms period (~100 Hz).
/// This manager runs a dedicated thread targeting a 5 ms tick (~200 Hz)
/// using a Stopwatch-based spin-sleep to avoid OS scheduler jitter.
///
/// Protocol:
///   DS → FMS status:  UDP 1160  (FMS listens; DS embeds team ID in each packet)
///   FMS → DS control: UDP 1121  (FMS sends 22-byte control packets)
///   DS → FMS TCP:     TCP 1750  (DS connects first; station assignment handshake)
/// </summary>
public sealed class DriverStationManager : BackgroundService
{
    // ── Timing ─────────────────────────────────────────────────────────────────

    private static readonly TimeSpan LoopPeriod     = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan UdpLinkTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TcpReadTimeout = TimeSpan.FromSeconds(5);

    // ── Ports ──────────────────────────────────────────────────────────────────

    private const int FmsUdpListenPort = 1160;
    private const int DsUdpReceivePort = 1121;
    private const int FmsTcpListenPort = 1750;

    // ── State ──────────────────────────────────────────────────────────────────

    public FrozenDictionary<AllianceStation, DriverStationConnection> Stations { get; }

    private readonly Arena.Arena _arena;
    private readonly ILogger<DriverStationManager> _logger;
    private static readonly TimeSpan LoopTimingWindow = TimeSpan.FromSeconds(30);

    private readonly object _loopTimingLock = new();
    private readonly Queue<(long Timestamp, double DurationMs)> _loopTimingSamples = new();
    private double _currentLoopMs;
    private double _maxLoopMs30s;

    private Socket?            _udpSocket;
    private CancellationToken  _ct;

    // Completion signals so StopAsync can wait for both loops to fully clean up.
    private readonly TaskCompletionSource _controlLoopDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task                          _tcpListenerTask  = Task.CompletedTask;

    public DriverStationManager(Arena.Arena arena, ILogger<DriverStationManager> logger)
    {
        _arena  = arena;
        _logger = logger;
        Stations = AllianceStations.All
            .ToFrozenDictionary(s => s, s => new DriverStationConnection(s));
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public DriverStationConnection this[AllianceStation station] => Stations[station];

    public (double CurrentMs, double MaxMs30s) GetLoopTimingSnapshot()
    {
        lock (_loopTimingLock)
            return (_currentLoopMs, _maxLoopMs30s);
    }

    /// <summary>Raised whenever any station's team assignment changes.</summary>
    public event Action? TeamAssignmentsChanged;

    /// <summary>
    /// Assigns a team to a station and fires <see cref="TeamAssignmentsChanged"/>.
    /// Pass teamNumber = 0 to clear the slot.
    /// </summary>
    public void AssignTeam(AllianceStation station, int teamNumber, string wpaKey = "")
    {
        if (teamNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(teamNumber), "Team number cannot be negative.");

        // if wpaKey is empty, set it to "possum2718" as a default until we get a radio programming setup
        if (string.IsNullOrEmpty(wpaKey))
            wpaKey = "possum2718";

        var ds = Stations[station];
        ds.TeamNumber = teamNumber;
        ds.WpaKey     = wpaKey;
        TeamAssignmentsChanged?.Invoke();
    }

    public void Estop(AllianceStation station)  => Stations[station].Estop = true;

    public void Astop(AllianceStation station)
    {
        if (_arena.Phase != MatchPhase.Auto)
            return;

        Stations[station].Astop = true;
    }

    public void AstopAll()
    {
        if (_arena.Phase != MatchPhase.Auto)
            return;

        foreach (var ds in Stations.Values)
            ds.Astop = true;
    }

    public void ResetStops(AllianceStation station)
    {
        Stations[station].Estop = false;
        Stations[station].Astop = false;
    }

    public void SetBypass(AllianceStation station, bool bypassed)
        => Stations[station].Bypassed = bypassed;

    /// <summary>Clears all e-stops and a-stops on every station (called on Clear Match).</summary>
    public void ResetAllStops()
    {
        foreach (var ds in Stations.Values)
        {
            ds.Estop = false;
            ds.Astop = false;
        }
    }

    // ── BackgroundService ──────────────────────────────────────────────────────

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        _ct = ct;
        _arena.GameDataChanged += OnGameDataChanged;
        _arena.PhaseChanged += OnArenaPhaseChanged;

        // UDP control loop: dedicated high-priority thread.
        // We wrap it so StopAsync can await full cleanup (socket dispose) on shutdown.
        new Thread(() =>
        {
            try
            {
                RunControlLoop(ct);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex,
                    "DS control loop crashed unexpectedly and has stopped. Driver Station networking is unavailable.");
            }
            finally { _controlLoopDone.TrySetResult(); }
        })
        {
            Name         = "DS-ControlLoop",
            Priority     = ThreadPriority.AboveNormal,
            IsBackground = false, // Must not be background — OS must wait for Dispose.
        }.Start();

        // TCP accept loop: not timing-critical, runs on thread pool.
        _tcpListenerTask = Task.Run(() => RunTcpListenerAsync(ct), ct);

        return Task.CompletedTask;
    }

    internal void OnArenaPhaseChanged(MatchPhase phase)
    {
        if (phase != MatchPhase.Teleop) return;

        foreach (var ds in Stations.Values)
            ds.Astop = false;
    }

    /// <summary>
    /// Waits for both the UDP control-loop thread and the TCP listener to finish
    /// so that sockets are fully closed before the process exits.
    /// Without this, Ctrl+C would leave port 1160/1750 bound and the next
    /// dotnet run would fail with WSAEADDRINUSE (10048).
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Signal cancellation (base implementation does this).
        await base.StopAsync(cancellationToken);

        // Wait for the UDP thread and TCP task to fully exit and dispose sockets.
        // Use a 5 s safety timeout so a hung loop doesn't block the host forever.
        var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        await Task.WhenAny(_controlLoopDone.Task, timeout);
        await Task.WhenAny(_tcpListenerTask, timeout);
    }

    // ── UDP control loop ───────────────────────────────────────────────────────

    private void RunControlLoop(CancellationToken ct)
    {
        _udpSocket = TryCreateAndBindUdpSocket();
        if (_udpSocket is null)
        {
            _logger.LogError(
                "Driver Station UDP control loop disabled because port {Port} could not be bound.",
                FmsUdpListenPort);
            return;
        }

        _udpSocket.Blocking = false;

        _logger.LogInformation("DS control loop started (target {Period} ms).", LoopPeriod.TotalMilliseconds);

        var sw   = Stopwatch.StartNew();
        var next = sw.Elapsed + LoopPeriod;

        while (!ct.IsCancellationRequested)
        {
            var loopStart = Stopwatch.GetTimestamp();

            DrainUdpReceiveBuffer();
            CheckUdpLinkTimeouts();
            SendControlPackets();
            _arena.Tick();

            SpinUntil(sw, next);
            next += LoopPeriod;

            var loopEnd = Stopwatch.GetTimestamp();
            var loopMs = (loopEnd - loopStart) * 1000.0 / Stopwatch.Frequency;
            RecordLoopTiming(loopEnd, loopMs);
        }

        _udpSocket.Dispose();
        _logger.LogInformation("DS control loop stopped.");
    }

    private Socket? TryCreateAndBindUdpSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, FmsUdpListenPort));
            return socket;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            _logger.LogError(ex,
                "Access denied while binding UDP port {Port}. On Windows this is usually a reserved/excluded port range or security policy. " +
                "Run 'netsh int ipv4 show excludedportrange protocol=udp' and choose a non-excluded port for local testing, " +
                "or run with elevated privileges if required by policy.",
                FmsUdpListenPort);
            socket.Dispose();
            return null;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            _logger.LogError(ex,
                "UDP port {Port} is already in use by another process. Stop the conflicting process or change the DS port.",
                FmsUdpListenPort);
            socket.Dispose();
            return null;
        }
    }

    // ── UDP receive ────────────────────────────────────────────────────────────

    private readonly byte[] _rxBuf = new byte[1500];

    private void DrainUdpReceiveBuffer()
    {
        if (_udpSocket is null) return;

        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            int bytes;
            try
            {
                bytes = _udpSocket.ReceiveFrom(_rxBuf, ref remote);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                break;
            }

            if (bytes < 8) continue;

            ParseStatusPacket(_rxBuf.AsSpan(0, bytes));
        }
    }

    internal void ParseStatusPacket(ReadOnlySpan<byte> packet)
    {
        // DS → FMS UDP status packet layout:
        //   [0-1]  Sequence number (not used by FMS)
        //   [2]    ?
        //   [3]    Status flags: 0x08 = RioLinked, 0x10 = RadioLinked, 0x20 = RobotLinked
        //   [4-5]  Team number, big-endian
        //   [6]    Battery voltage integer part (volts)
        //   [7]    Battery voltage fractional part (add value/256 V)
        //   [8+]   Variable-length tags: [length] [type] [data * (length-1)]
        //            Tag 1, length 6: [lost_hi] [lost_lo] [?] [?] [trip_ms]

        int teamNumber = (packet[4] << 8) | packet[5];
        var ds = FindStationByTeamNumber(teamNumber);
        if (ds is null) return;

        ds.LastPacketTime = DateTime.UtcNow;
        ds.DsLinked       = true;
        ds.RioLinked      = (packet[3] & 0x08) != 0;
        ds.RadioLinked    = (packet[3] & 0x10) != 0;
        ds.RobotLinked    = (packet[3] & 0x20) != 0;

        if (ds.RobotLinked)
        {
            ds.LastRobotLinkedTime = DateTime.UtcNow;
            ds.BatteryVoltage      = packet[6] + packet[7] / 256.0;
        }

        // Parse tags.
        int i = 8;
        while (i < packet.Length)
        {
            byte length = packet[i++];
            if (length == 0) continue;
            if (i + length > packet.Length) break;

            byte tagType = packet[i];
            if (tagType == 1 && length == 6)
            {
                ds.MissedPacketCount = (packet[i + 1] << 8) | packet[i + 2];
                ds.DsRobotTripTimeMs = packet[i + 5];
            }

            i += length;
        }
    }

    private DriverStationConnection? FindStationByTeamNumber(int teamNumber)
    {
        foreach (var ds in Stations.Values)
            if (ds.TeamNumber == teamNumber) return ds;
        return null;
    }

    internal static int? TryGetTeamNumberFromDriverStationIp(IPAddress ipAddress)
    {
        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6 && ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
            return null;

        byte[] ipBytes = ipAddress.GetAddressBytes();
        if (ipBytes.Length < 3)
            return null;

        return ipBytes[1] * 100 + ipBytes[2];
    }

    // ── UDP send ───────────────────────────────────────────────────────────────

    private readonly byte[] _txBuf = new byte[22];

    private void SendControlPackets()
    {
        if (_udpSocket is null) return;

        foreach (var ds in Stations.Values)
        {
            if (ds.UdpEndpoint is null) continue;

            EncodeControlPacket(_txBuf, ds);

            try
            {
                _udpSocket.SendTo(_txBuf, ds.UdpEndpoint);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning("UDP send failed for {Station}: {Error}", ds.Station, ex.SocketErrorCode);
            }

            ds.TxSequence++;
        }
    }

    internal void EncodeControlPacket(byte[] buf, DriverStationConnection ds)
    {
        // FMS → DS UDP control packet (22 bytes), per cheesy-arena protocol:
        //   [0-1]  Sequence number (big-endian, incremented each packet)
        //   [2]    Protocol version (0)
        //   [3]    Control byte:
        //            0x02 = Autonomous mode
        //            0x04 = Enabled
        //            0x40 = AStop (autonomous stop)
        //            0x80 = EStop
        //   [4]    0 (unused)
        //   [5]    Alliance station index: 0=R1, 1=R2, 2=R3, 3=B1, 4=B2, 5=B3
        //   [6]    Match type: 0=none, 1=practice, 2=qualification, 3=playoff
        //   [7-8]  Match number (big-endian)
        //   [9]    Match repeat number
        //   [10-13] Microseconds within current second (big-endian)
        //           Equivalent to Go's: time.Now().Nanosecond() / 1000
        //   [14]   Seconds
        //   [15]   Minutes
        //   [16]   Hours
        //   [17]   Day
        //   [18]   Month
        //   [19]   Year - 1900
        //   [20-21] Seconds remaining in current phase (big-endian)

        Array.Clear(buf);

        buf[0] = (byte)(ds.TxSequence >> 8);
        buf[1] = (byte)(ds.TxSequence & 0xFF);
        buf[2] = 0; // protocol version

        bool estop   = ds.Estop || _arena.ArenaEstop;
        bool enabled = !estop && !ds.Astop && _arena.IsMatchRunning && !ds.Bypassed;
        bool auto    = _arena.Phase == MatchPhase.Auto;

        byte control = 0;
        if (auto)     control |= 0x02;
        if (enabled)  control |= 0x04;
        if (ds.Astop) control |= 0x40;
        if (estop)    control |= 0x80;
        buf[3] = control;

        buf[5] = (byte)((ds.Station.Color == AllianceColor.Red ? 0 : 3) + (int)ds.Station.Position - 1);
        buf[6] = (byte)_arena.MatchType;
        buf[7] = (byte)(_arena.MatchNumber >> 8);
        buf[8] = (byte)(_arena.MatchNumber & 0xFF);
        buf[9] = (byte)_arena.MatchRepeat;

        var  now = DateTime.Now;
        long us  = now.Millisecond * 1000L + now.Microsecond; // microseconds within current second
        buf[10] = (byte)((us >> 24) & 0xFF);
        buf[11] = (byte)((us >> 16) & 0xFF);
        buf[12] = (byte)((us >>  8) & 0xFF);
        buf[13] = (byte)( us        & 0xFF);
        buf[14] = (byte)now.Second;
        buf[15] = (byte)now.Minute;
        buf[16] = (byte)now.Hour;
        buf[17] = (byte)now.Day;
        buf[18] = (byte)now.Month;
        buf[19] = (byte)(now.Year - 1900);

        int secsRemaining = (int)_arena.TimeRemaining.TotalSeconds;
        buf[20] = (byte)(secsRemaining >> 8);
        buf[21] = (byte)(secsRemaining & 0xFF);
    }

    // ── Link timeouts ──────────────────────────────────────────────────────────

    private void CheckUdpLinkTimeouts()
    {
        var now = DateTime.UtcNow;
        foreach (var ds in Stations.Values)
        {
            if (ds.DsLinked && (now - ds.LastPacketTime) > UdpLinkTimeout)
            {
                ds.DsLinked       = false;
                ds.RadioLinked    = false;
                ds.RioLinked      = false;
                ds.RobotLinked    = false;
                ds.BatteryVoltage = 0;
                _logger.LogWarning("DS {Station} (team {Team}) UDP link timed out.", ds.Station, ds.TeamNumber);
            }

            ds.SecondsSinceLastRobotLink = (now - ds.LastRobotLinkedTime).TotalSeconds;
        }
    }

    // ── TCP listener ───────────────────────────────────────────────────────────

    private async Task RunTcpListenerAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, FmsTcpListenPort);
        try
        {
            listener.Start();
            _logger.LogInformation("Listening for driver stations on TCP port {Port}.", FmsTcpListenPort);

            while (!ct.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleTcpConnectionAsync(tcpClient, ct), ct);
            }
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex,
                "Failed to start Driver Station TCP listener on port {Port}. Driver Station networking is unavailable.",
                FmsTcpListenPort);
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task HandleTcpConnectionAsync(TcpClient tcpClient, CancellationToken ct)
    {
        var remoteIp = ((IPEndPoint)tcpClient.Client.RemoteEndPoint!).Address;
        var stream   = tcpClient.GetStream();
        stream.ReadTimeout = (int)TcpReadTimeout.TotalMilliseconds;

        DriverStationConnection? ds = null;
        try
        {
            // ── Handshake: read initial DS→FMS identification packet ───────────
            // Format: [0x00, 0x03, 0x18, teamHi, teamLo]
            //   size = 3 (2-byte big-endian), type = 24 (0x18), team number
            var initBuf = new byte[5];
            if (!await ReadExactAsync(stream, initBuf, ct)) return;

            if (initBuf[0] != 0x00 || initBuf[1] != 0x03 || initBuf[2] != 0x18)
            {
                _logger.LogWarning("Invalid handshake from {IP} — dropping.", remoteIp);
                return;
            }

            int teamNumber = (initBuf[3] << 8) | initBuf[4];

            ds = FindStationByTeamNumber(teamNumber);
            if (ds is null)
            {
                _logger.LogWarning("Team {Team} not in current match — closing in 1 s.", teamNumber);
                await Task.Delay(1000, ct);
                return;
            }

            // ── Wrong-station detection ────────────────────────────────────────
            // DS IPs follow 10.TE.AM.x, e.g. team 2718 → 10.27.18.x
            byte stationStatus = 0x00;
            ds.WrongStation = string.Empty;

            int? ipTeamNumber = TryGetTeamNumberFromDriverStationIp(remoteIp);
            if (ipTeamNumber is int stationTeamNumber && stationTeamNumber != teamNumber)
            {
                var wrongDs = FindStationByTeamNumber(stationTeamNumber);
                if (wrongDs is not null)
                {
                    ds.WrongStation = wrongDs.Station.ToString();
                    stationStatus   = 0x01;
                    _logger.LogWarning("Team {Team} is plugged into wrong station {Wrong}.",
                        teamNumber, wrongDs.Station);
                }
            }

            // ── Send station assignment packet ─────────────────────────────────
            // Format: [packetSize, packetSize, packetType, stationIndex, stationStatus]
            byte stationIndex = (byte)((ds.Station.Color == AllianceColor.Red ? 0 : 3) + (int)ds.Station.Position - 1);
            await stream.WriteAsync(new byte[] { 0x00, 0x03, 0x19, stationIndex, stationStatus }, ct);

            ds.TcpClient   = tcpClient;
            ds.UdpEndpoint = new IPEndPoint(remoteIp, DsUdpReceivePort);
            ds.TeamNumber  = teamNumber;

            _logger.LogInformation("Team {Team} connected in station {Station} ({IP}).",
                teamNumber, ds.Station, remoteIp);

            // Send current game data if already set (e.g., DS reconnects after auto).
            if (_arena.GameData.Length > 0)
                await SendGameDataAsync(ds.Station, _arena.GameData, ct);

            // ── TCP read loop ──────────────────────────────────────────────────
            await RunTcpReadLoopAsync(ds, stream, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning("TCP error from {IP}: {Error}", remoteIp, ex.Message);
        }
        finally
        {
            if (ds is not null)
            {
                ds.TcpClient   = null;
                ds.UdpEndpoint = null;
                ds.DsLinked    = false;
                ds.RobotLinked = false;
                ds.RadioLinked = false;
                ds.RioLinked   = false;
                _logger.LogInformation("DS {Station} (team {Team}) TCP disconnected.", ds.Station, ds.TeamNumber);
            }
            tcpClient.Dispose();
        }
    }

    private static async Task RunTcpReadLoopAsync(
        DriverStationConnection ds, NetworkStream stream, CancellationToken ct)
    {
        // TCP packets: 2-byte big-endian length prefix followed by payload.
        //   Payload[0] = packet type
        //     29 (0x1D) = Keepalive       — ignore
        //     22 (0x16) = Robot log data  — ignore for now
        var sizeBuf = new byte[2];
        var dataBuf = new byte[65535];

        while (!ct.IsCancellationRequested)
        {
            if (!await ReadExactAsync(stream, sizeBuf, ct)) break;

            int length = (sizeBuf[0] << 8) | sizeBuf[1];
            if (length == 0) continue;
            if (length > dataBuf.Length) break; // Malformed.

            if (!await ReadExactAsync(stream, dataBuf.AsMemory(0, length), ct)) break;

            // int packetType = dataBuf[0];
            // All currently known types (29=keepalive, 22=robot log) require no action.
        }
    }

    // ── TCP game data ──────────────────────────────────────────────────────────

    private void OnGameDataChanged(string data)
    {
        foreach (var station in Stations.Keys)
            _ = SendGameDataAsync(station, data, _ct);
    }

    /// <summary>
    /// Sends game-specific data to a driver station over TCP (type 28).
    /// The DS forwards this string to the robot via DriverStation.getGameSpecificMessage().
    /// </summary>
    public async Task SendGameDataAsync(AllianceStation station, string gameData, CancellationToken ct = default)
    {
        var ds = Stations[station];
        if (ds.TcpClient?.GetStream() is not { } stream) return;

        byte[] payload = System.Text.Encoding.UTF8.GetBytes(gameData);
        var    packet  = new byte[payload.Length + 4];
        packet[0] = 0x00;
        packet[1] = (byte)(payload.Length + 2); // size = type byte + data-length byte + data
        packet[2] = 0x1C;                        // packet type 28: game data
        packet[3] = (byte)payload.Length;
        payload.CopyTo(packet, 4);

        try { await stream.WriteAsync(packet, ct); }
        catch (Exception ex)
        {
            ds.TcpClient = null;
            _ = ex; // logged by the read loop when it also detects the error
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buf, CancellationToken ct)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = await stream.ReadAsync(buf[total..], ct);
            if (n == 0) return false; // Remote closed.
            total += n;
        }
        return true;
    }

    private static Task<bool> ReadExactAsync(NetworkStream stream, byte[] buf, CancellationToken ct) =>
        ReadExactAsync(stream, buf.AsMemory(), ct);

    private static void SpinUntil(Stopwatch sw, TimeSpan target)
    {
        // Sleep for most of the remaining time, then busy-spin the last millisecond
        // to avoid OS timer imprecision from overshooting the target.
        var remaining = target - sw.Elapsed;
        if (remaining > TimeSpan.FromMilliseconds(2))
            Thread.Sleep(remaining - TimeSpan.FromMilliseconds(1));

        while (sw.Elapsed < target)
            Thread.SpinWait(10);
    }

    private void RecordLoopTiming(long nowTimestamp, double durationMs)
    {
        var minTimestamp = nowTimestamp - (long)(LoopTimingWindow.TotalSeconds * Stopwatch.Frequency);

        lock (_loopTimingLock)
        {
            _currentLoopMs = durationMs;
            _loopTimingSamples.Enqueue((nowTimestamp, durationMs));

            while (_loopTimingSamples.Count > 0 && _loopTimingSamples.Peek().Timestamp < minTimestamp)
                _loopTimingSamples.Dequeue();

            var maxLoopMs = 0.0;
            foreach (var sample in _loopTimingSamples)
                if (sample.DurationMs > maxLoopMs)
                    maxLoopMs = sample.DurationMs;

            _maxLoopMs30s = maxLoopMs;
        }
    }
}
