using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PossumFMS.Core.Arena;

namespace PossumFMS.Core.DriverStation;

/// <summary>
/// Manages all six driver station connections and runs the high-frequency
/// FMS control loop.
///
/// This implementation runs a dedicated thread targeting a 5 ms tick (~200 Hz)
/// using a Stopwatch-based spin-sleep to avoid OS scheduler jitter.
///
/// Protocol:
///   DS → FMS status:  UDP 1160  (FMS listens; DS embeds team ID in each packet)
///   FMS → DS UDP:     UDP 1121  (control/context packet)
///   DS ↔ FMS TCP:     TCP 1750  (team number, station info, event code, game data)
/// </summary>
public sealed class DriverStationManager : BackgroundService
{
    // ── Timing ─────────────────────────────────────────────────────────────────

    private static readonly TimeSpan LoopPeriod     = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan UdpLinkTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TcpReadTimeout = TimeSpan.FromSeconds(5);

    // ── Ports & Protocol Tags ──────────────────────────────────────────────────

    private const int DsUdpReceivePort = 1121;
    private const int FmsTcpListenPort = 1750;
    private const string DriverStationEventCode = "POSM";

    // Protocol Tags:
    // Legacy NI Driver Station:
    //   Tag 24 (0x18) - Initial handshake: [0x00, 0x03, 0x18, teamHi, teamLo]
    //   Tag 25 (0x19) - Station assignment: [0x00, 0x03, 0x19, stationIndex, stationStatus]
    //   Tag 28 (0x1C) - Game data (TCP): [0x00, len+2, 0x1C, len, ...data...]
    // 2027 FIRST Driver Station:
    //   Tag 30 (0x1E) - Initial handshake: [lenHi, lenLo, 0x1E, udpPortHi, udpPortLo, flags, teamLen, ...teamAscii...]
    //   Tag 31 (0x1F) - Station assignment: [0x00, 0x06, 0x1F, stationIndex, stationStatus, flags, teamHi, teamLo]
    //   Tag 32 (0x20) - Game data (UDP control packet tag): [len+1, 0x20, ...data (max 8 bytes)...]
    private const byte LegacyDsHandshakeTag = 0x18;
    private const byte LegacyDsStationAssignmentTag = 0x19;
    private const byte LegacyDsGameDataTag = 0x1C;
    private const byte NewDsHandshakeTag = 0x1E;
    private const byte NewDsStationAssignmentTag = 0x1F;
    private const byte NewDsGameDataTag = 0x20;

    // ── State ──────────────────────────────────────────────────────────────────

    public FrozenDictionary<AllianceStation, DriverStationConnection> Stations { get; }

    private readonly Arena.Arena _arena;
    private readonly ILogger<DriverStationManager> _logger;
    private static readonly TimeSpan LoopTimingWindow = TimeSpan.FromSeconds(30);

    private readonly object _loopTimingLock = new();
    private readonly object _stationStateLock = new();
    private readonly object _controlHealthLock = new();
    private readonly Queue<(long Timestamp, double DurationMs)> _loopTimingSamples = new();
    private readonly Func<IDriverStationUdpTransport> _udpTransportFactory;
    private double _currentLoopMs;
    private double _maxLoopMs30s;

    private IDriverStationUdpTransport? _udpTransport;
    private DriverStationControlHealth _controlHealth = new(
        DriverStationControlState.Stopped,
        TransportAvailable: false,
        LastFaultUtc: null,
        LastFault: null);
    private CancellationToken  _ct;

    // Completion signals so StopAsync can wait for both loops to fully clean up.
    private readonly TaskCompletionSource _controlLoopDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task                          _tcpListenerTask  = Task.CompletedTask;

    // ── Practice mode override ─────────────────────────────────────────────────
    // When Free Practice is enabled the FMS normally does not send control packets.
    // To allow remote disable / e-stop we temporarily resume sending for a short
    // window so the DS receives the stop signal, then go quiet again.
    private static readonly TimeSpan PracticeModeOverrideDuration = TimeSpan.FromSeconds(1);
    private DateTime _practiceModeOverrideUntil = DateTime.MinValue;

    public DriverStationManager(Arena.Arena arena, ILogger<DriverStationManager> logger)
        : this(arena, logger, () => new SocketDriverStationUdpTransport())
    {
    }

    internal DriverStationManager(
        Arena.Arena arena,
        ILogger<DriverStationManager> logger,
        Func<IDriverStationUdpTransport> udpTransportFactory)
    {
        _arena  = arena;
        _logger = logger;
        _udpTransportFactory = udpTransportFactory;
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

    public DriverStationControlHealth GetControlHealthSnapshot()
    {
        lock (_controlHealthLock)
            return _controlHealth;
    }

    public bool IsControlChannelOperational()
    {
        lock (_controlHealthLock)
            return _controlHealth.State == DriverStationControlState.Operational
                && _controlHealth.TransportAvailable;
    }

    public bool ResetControlFault()
    {
        if (_arena.Phase != MatchPhase.Idle)
            return false;

        lock (_controlHealthLock)
        {
            if (_controlHealth.State != DriverStationControlState.Faulted
                || !_controlHealth.TransportAvailable)
                return false;

            _controlHealth = new DriverStationControlHealth(
                DriverStationControlState.Operational,
                TransportAvailable: true,
                LastFaultUtc: null,
                LastFault: null);
            return true;
        }
    }

    public IReadOnlyList<string> GetMatchStartReadinessFailures()
    {
        var failures = new List<string>();
        if (!IsControlChannelOperational())
            failures.Add("Driver Station UDP control channel is not operational.");

        lock (_stationStateLock)
        {
            foreach (var ds in Stations.Values)
            {
                if (ds.Bypassed)
                    continue;

                if (ds.TeamNumber <= 0)
                    failures.Add($"{ds.Station} has no assigned team.");
                else if (!ds.IsLinked)
                    failures.Add($"{ds.Station} is not linked to its robot.");
                else if (ds.TcpClient is null || ds.UdpEndpoint is null)
                    failures.Add($"{ds.Station} has no validated FMS control endpoint.");
                else if (ds.Estop)
                    failures.Add($"{ds.Station} is e-stopped.");
            }
        }

        return failures;
    }

    /// <summary>Raised whenever any station's team assignment changes.</summary>
    public event Action? TeamAssignmentsChanged;

    public sealed record TeamAssignment(AllianceStation Station, int TeamNumber, string WpaKey = "");

    /// <summary>
    /// Applies a set of team assignments atomically.
    /// Stations not present in <paramref name="assignments"/> are left unchanged.
    /// Duplicate non-zero team numbers are not allowed.
    /// </summary>
    public void AssignTeams(IReadOnlyList<TeamAssignment> assignments)
    {
        // Team mappings are only mutable while the field is idle so station identity
        // cannot change mid-match.
        if (_arena.Phase != MatchPhase.Idle)
            throw new InvalidOperationException("Team assignments can only be changed while the arena is idle.");

        // Caller must provide an assignment collection (empty is allowed and is a no-op).
        if (assignments is null)
            throw new ArgumentNullException(nameof(assignments));

        if (assignments.Count == 0)
            return;

        // Validate each requested entry:
        //  - station key must be one of the 6 known field stations
        //  - team number must be non-negative (0 means "unassigned")
        foreach (var assignment in assignments)
        {
            if (!Stations.ContainsKey(assignment.Station))
                throw new ArgumentException($"Unknown alliance station '{assignment.Station}'.", nameof(assignments));

            if (assignment.TeamNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(assignments), $"Team number for station {assignment.Station} cannot be negative.");
        }

        var latestByStation = new Dictionary<AllianceStation, TeamAssignment>();
        foreach (var assignment in assignments)
            latestByStation[assignment.Station] = assignment;

        // Compute what the full station→team map would be after applying this partial
        // update so duplicate-team detection includes both changed and unchanged stations.
        var resultingTeamNumbers = new Dictionary<AllianceStation, int>();
        foreach (var station in AllianceStations.All)
            resultingTeamNumbers[station] = Stations[station].TeamNumber;

        foreach (var station in AllianceStations.All)
            if (latestByStation.TryGetValue(station, out var assignment))
                resultingTeamNumbers[station] = assignment.TeamNumber;

        var duplicateTeams = resultingTeamNumbers
            .Where(x => x.Value > 0)
            .GroupBy(x => x.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        // Reject any final state where the same non-zero team appears on >1 station.
        if (duplicateTeams.Length > 0)
            throw new InvalidOperationException($"Duplicate team assignments are not allowed: {string.Join(", ", duplicateTeams)}.");

        // if wpaKey is empty, set it to the team number twice (e.g. "27182718" for 2718) as a default until we get a radio programming setup
        static string NormalizeWpaKey(int teamNumber, string wpaKey)
        {
            if (teamNumber == 0)
                return string.Empty;

            return string.IsNullOrEmpty(wpaKey) ? $"{teamNumber}{teamNumber}" : wpaKey;
        }

        var changed = false;

        lock (_stationStateLock)
        {
            // Apply only stations present in this request. If the team number changes,
            // clear active DS network state so stale endpoint/socket ownership cannot persist.
            foreach (var station in AllianceStations.All)
            {
                if (!latestByStation.TryGetValue(station, out var assignment))
                    continue;

                var ds = Stations[station];
                var normalizedWpaKey = NormalizeWpaKey(assignment.TeamNumber, assignment.WpaKey);
                var teamChanged = ds.TeamNumber != assignment.TeamNumber;
                var keyChanged = ds.WpaKey != normalizedWpaKey;

                if (!teamChanged && !keyChanged)
                    continue;

                if (teamChanged)
                    ClearStationConnectionState(ds, "team assignment changed", closeTcpClient: true);

                ds.TeamNumber = assignment.TeamNumber;
                ds.WpaKey = normalizedWpaKey;
                changed = true;
            }
        }

        if (changed)
            TeamAssignmentsChanged?.Invoke();
    }

    public void Estop(AllianceStation station)
    {
        Stations[station].Estop = true;
        if (_arena.FreePracticeEnabled)
            TriggerPracticeModeOverride();
    }

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

    /// <summary>
    /// Temporarily resumes sending FMS control packets while in Free Practice mode
    /// so the DS receives a disable signal. The robot will be disabled for the
    /// duration of the override window, then the FMS goes quiet and the DS
    /// returns to standalone control.
    /// </summary>
    public void PracticeModeDisable(AllianceStation station)
    {
        if (!_arena.FreePracticeEnabled)
            return;

        TriggerPracticeModeOverride();
        _logger.LogInformation("Practice mode disable triggered for {Station}.", station);
    }

    /// <summary>Triggers the practice mode override for all stations.</summary>
    public void PracticeModeDisableAll()
    {
        if (!_arena.FreePracticeEnabled)
            return;

        TriggerPracticeModeOverride();
        _logger.LogInformation("Practice mode disable triggered for all stations.");
    }

    private void TriggerPracticeModeOverride()
    {
        _practiceModeOverrideUntil = DateTime.UtcNow + PracticeModeOverrideDuration;
    }

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
        while (!ct.IsCancellationRequested)
        {
            IDriverStationUdpTransport? transport = null;
            try
            {
                SetControlStarting();
                transport = _udpTransportFactory();
                transport.Bind();
                _udpTransport = transport;
                SetTransportAvailable();

                _logger.LogInformation("DS control loop started (target {Period} ms).", LoopPeriod.TotalMilliseconds);
                RunBoundControlLoop(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                HandleControlFault(ex);
                ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
            }
            finally
            {
                if (ReferenceEquals(_udpTransport, transport))
                    _udpTransport = null;

                transport?.Dispose();
            }
        }

        SetControlStopped();
        _logger.LogInformation("DS control loop stopped.");
    }

    private void RunBoundControlLoop(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
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
    }

    // ── UDP receive ────────────────────────────────────────────────────────────

    private readonly byte[] _rxBuf = new byte[1500];

    private void DrainUdpReceiveBuffer()
    {
        var transport = _udpTransport ?? throw new InvalidOperationException("Driver Station UDP transport is unavailable.");

        while (true)
        {
            int bytes;
            IPEndPoint remoteEndpoint;
            try
            {
                bytes = transport.Receive(_rxBuf, out remoteEndpoint);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                break;
            }

            if (bytes < 8) continue;
            ParseStatusPacket(_rxBuf.AsSpan(0, bytes), remoteEndpoint);
        }
    }

    internal void ParseStatusPacket(ReadOnlySpan<byte> packet)
        => ParseStatusPacket(packet, remoteEndpoint: null);

    internal void ParseStatusPacket(ReadOnlySpan<byte> packet, IPEndPoint? remoteEndpoint)
    {
        // DS → FMS UDP status packet layout:
        //   [0-1]  Sequence number (not used by FMS)
        //   [2]    Protocol/comm version (FRCDocs observes 0x00)
        //   [3]    Status byte. PossumFMS currently consumes:
        //            0x08 = Rio ping, 0x10 = Radio ping, 0x20 = Robot comms active
        //   [4-5]  Team number, big-endian
        //   [6]    Battery voltage integer part (volts)
        //   [7]    Battery voltage fractional part (add value/256 V)
        //   [8+]   Variable-length tags: [length] [type] [data * (length-1)]
        //            Tag 0x01 (Comms Metrics), length 6:
        //              [lost_hi] [lost_lo] [sent_hi] [sent_lo] [trip_ms]

        int teamNumber = (packet[4] << 8) | packet[5];
        var ds = FindStationByTeamNumber(teamNumber);
        if (ds is null) return;
        if (remoteEndpoint is not null
            && (ds.ValidatedEndpoint is null || !ds.ValidatedEndpoint.Address.Equals(remoteEndpoint.Address)))
            return;

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
        if (teamNumber <= 0)
            return null;

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

    private readonly byte[] _txBuf = new byte[64];

    private void SendControlPackets()
    {
        var transport = _udpTransport ?? throw new InvalidOperationException("Driver Station UDP transport is unavailable.");

        bool practiceOverride = _arena.FreePracticeEnabled && DateTime.UtcNow < _practiceModeOverrideUntil;
        if (!IsDriverStationCommunicationEnabled() && !practiceOverride) return;

        foreach (var ds in Stations.Values)
        {
            if (ds.UdpEndpoint is null) continue;

            int packetLen = EncodeControlPacket(_txBuf, ds);

            transport.Send(_txBuf.AsSpan(0, packetLen), ds.UdpEndpoint);

            ds.TxSequence++;
        }
    }

    internal int EncodeControlPacket(byte[] buf, DriverStationConnection ds)
    {
        // FMS → DS UDP packet layout:
        // Base (bytes 0-21, common to Legacy NI DS and 2027 DS):
        //   [0-1]   Sequence number (big-endian, incremented each packet)
        //   [2]     Protocol version (0)
        //   [3]     Control byte:
        //             0x02 = Autonomous mode
        //             0x04 = Enabled
        //             0x40 = AStop (autonomous stop)
        //             0x80 = EStop
        //   [4]     Request byte (currently 0)
        //   [5]     Alliance station index: 0=R1, 1=R2, 2=R3, 3=B1, 4=B2, 5=B3
        //   [6]     Tournament level: 0=match test, 1=practice, 2=qualification, 3=playoff
        //   [7-8]   Match number (big-endian)
        //   [9]     Play/replay number
        //   [10-13] Microseconds within current second (big-endian)
        //   [14-19] Date/time fields
        //   [14]    Seconds
        //   [15]    Minutes
        //   [16]    Hours
        //   [17]    Day
        //   [18]    Month
        //   [19]    Year - 1900
        //   [20-21] Seconds remaining in current phase (big-endian)
        // 2027 DS Extensions (bytes 22+):
        //   Tag 32 (0x20) - Game Data: [tag_len, 0x20, ...data (max 8 bytes)...]
        //   where tag_len = data_len + 1 (includes tag byte itself)

        Array.Clear(buf);

        buf[0] = (byte)(ds.TxSequence >> 8);
        buf[1] = (byte)(ds.TxSequence & 0xFF);
        buf[2] = 0; // protocol version

        bool estop   = ds.Estop || _arena.ArenaEstop;
        bool enabled = ds.TeamNumber > 0 && !estop && !ds.Astop && _arena.IsMatchRunning && !ds.Bypassed && IsDriverStationCommunicationEnabled();
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
        buf[18] = (byte)(now.Month - 1); // DS expects month in range 0-11
        buf[19] = (byte)(now.Year - 1900);

        int secsRemaining = (int)_arena.TimeRemaining.TotalSeconds;
        buf[20] = (byte)(secsRemaining >> 8);
        buf[21] = (byte)(secsRemaining & 0xFF);

        int packetLength = 22;

        // In 2027 DS protocol, game data is delivered inside the UDP control packet as Tag 32 (max 8 bytes)
        if (ds.IsNewDs && !string.IsNullOrEmpty(_arena.GameData))
        {
            byte[] gameDataBytes = Encoding.UTF8.GetBytes(_arena.GameData);
            int gameDataLen = Math.Min(gameDataBytes.Length, 8);
            if (gameDataLen > 0)
            {
                buf[22] = (byte)(gameDataLen + 1); // length of tag payload including tag byte
                buf[23] = NewDsGameDataTag;        // Tag 32 (0x20)
                gameDataBytes.AsSpan(0, gameDataLen).CopyTo(buf.AsSpan(24));
                packetLength += 2 + gameDataLen;
            }
        }

        return packetLength;
    }

    internal static byte[] CreateStationInfoPacket(
        AllianceStation station,
        byte stationStatus,
        int teamNumber = 0,
        bool isNewDs = false,
        byte flags = 0)
    {
        byte stationIndex = (byte)((station.Color == AllianceColor.Red ? 0 : 3) + (int)station.Position - 1);
        if (!isNewDs)
        {
            return CreateTcpPacket(LegacyDsStationAssignmentTag, [stationIndex, stationStatus]);
        }

        return CreateTcpPacket(NewDsStationAssignmentTag, [
            stationIndex,
            stationStatus,
            flags,
            (byte)(teamNumber >> 8),
            (byte)(teamNumber & 0xFF)
        ]);
    }

    internal static byte[] CreateRejectionPacket(byte status, bool isNewDs)
    {
        if (!isNewDs)
        {
            return CreateTcpPacket(LegacyDsStationAssignmentTag, [0x00, status]);
        }

        return CreateTcpPacket(NewDsStationAssignmentTag, [
            0x00,   // stationIndex = 0
            status, // rejection status: 2 = not in match, 3 = invalid/malformed
            0x00,   // flags = 0
            0x00,   // teamHi = 0
            0x00    // teamLo = 0
        ]);
    }

    internal static bool TryParseInitialHandshake(
        ReadOnlySpan<byte> payload,
        out bool isNewDs,
        out int teamNumber,
        out int udpSendPort,
        out byte flags)
    {
        isNewDs = false;
        teamNumber = 0;
        udpSendPort = DsUdpReceivePort;
        flags = 0;

        if (payload.Length < 3)
            return false;

        byte tag = payload[0];

        if (tag == LegacyDsHandshakeTag)
        {
            if (payload.Length != 3)
                return false;

            isNewDs = false;
            udpSendPort = DsUdpReceivePort;
            teamNumber = (payload[1] << 8) | payload[2];
            return teamNumber > 0 && teamNumber <= 65535;
        }

        if (tag == NewDsHandshakeTag)
        {
            if (payload.Length < 5)
                return false;

            isNewDs = true;
            udpSendPort = (payload[1] << 8) | payload[2];
            flags = payload[3];
            int teamNumLen = payload[4];

            if (payload.Length < 5 + teamNumLen || teamNumLen == 0)
                return false;

            string teamStr = Encoding.ASCII.GetString(payload.Slice(5, teamNumLen));
            if (!int.TryParse(teamStr, out teamNumber) || teamNumber <= 0 || teamNumber > 65535)
                return false;

            return true;
        }

        return false;
    }

    internal static byte[] CreateEventCodePacket()
    {
        byte[] eventCodeBytes = Encoding.UTF8.GetBytes(DriverStationEventCode);
        var payload = new byte[eventCodeBytes.Length + 1];
        payload[0] = (byte)eventCodeBytes.Length;
        eventCodeBytes.CopyTo(payload, 1);
        return CreateTcpPacket(0x14, payload);
    }

    private static byte[] CreateTcpPacket(byte packetType, ReadOnlySpan<byte> payload)
    {
        int size = payload.Length + 1;
        var packet = new byte[size + 2];
        packet[0] = (byte)(size >> 8);
        packet[1] = (byte)(size & 0xFF);
        packet[2] = packetType;
        payload.CopyTo(packet.AsSpan(3));
        return packet;
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
        var ownsStation = false;
        try
        {
            // ── Handshake: read initial DS→FMS identification packet ───────────
            // A 2-byte big-endian length prefix precedes every tagged TCP packet.
            var lenBuf = new byte[2];
            if (!await ReadExactAsync(stream, lenBuf, ct)) return;

            int packetLen = (lenBuf[0] << 8) | lenBuf[1];
            if (packetLen < 3 || packetLen > 1024)
            {
                _logger.LogWarning("Invalid initial handshake length {Length} from {IP} — dropping.", packetLen, remoteIp);
                return;
            }

            var payload = new byte[packetLen];
            if (!await ReadExactAsync(stream, payload, ct)) return;

            if (!TryParseInitialHandshake(payload, out bool isNewDs, out int teamNumber, out int udpSendPort, out byte flags))
            {
                byte tag = payload[0];
                if (tag == NewDsHandshakeTag)
                {
                    _logger.LogWarning("[2027 DS] Malformed initial handshake from {IP}; sending rejection (status 3) and dropping.", remoteIp);
                    await stream.WriteAsync(CreateRejectionPacket(0x03, isNewDs: true), ct);
                    await Task.Delay(1000, ct);
                }
                else
                {
                    _logger.LogWarning("Invalid initial handshake (tag 0x{Tag:X2}, length {Length}) from {IP} — dropping.", tag, packetLen, remoteIp);
                }
                return;
            }

            if (isNewDs)
            {
                _logger.LogInformation("[2027 DS] Received connection from {IP} for Team {Team} (UDP send port: {Port}, flags: 0x{Flags:X2}).",
                    remoteIp, teamNumber, udpSendPort, flags);
            }
            else
            {
                _logger.LogInformation("[NI DS] Received legacy connection from {IP} for Team {Team}.",
                    remoteIp, teamNumber);
            }

            ds = FindStationByTeamNumber(teamNumber);
            if (ds is null)
            {
                string dsType = isNewDs ? "2027 DS" : "NI DS";
                _logger.LogWarning("[{DsType}] Team {Team} ({IP}) is not scheduled in current match — sending rejection (status 2) and closing in 1 s.",
                    dsType, teamNumber, remoteIp);
                await stream.WriteAsync(CreateRejectionPacket(0x02, isNewDs), ct);
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
                    _logger.LogWarning("[{DsType}] Team {Team} is plugged into wrong station {Wrong} (assigned: {Assigned}).",
                        isNewDs ? "2027 DS" : "NI DS", teamNumber, wrongDs.Station, ds.Station);
                }
            }

            // ── Send initial DS context ────────────────────────────────────────
            // Station info establishes the assigned station, status, and for 2027 DS echoes the team number.
            // Event code is a separate TCP tag that the DS displays in its UI.
            await stream.WriteAsync(CreateStationInfoPacket(ds.Station, stationStatus, teamNumber, isNewDs), ct);
            await stream.WriteAsync(CreateEventCodePacket(), ct);

            lock (_stationStateLock)
            {
                var endpoint = new IPEndPoint(remoteIp, udpSendPort);

                if (ds.TcpClient is not null
                    && ds.ValidatedEndpoint?.Address.Equals(remoteIp) != true)
                    throw new InvalidOperationException($"Station {ds.Station} already has an active Driver Station connection.");

                foreach (var other in Stations.Values)
                {
                    if (ReferenceEquals(other, ds))
                        continue;

                    if (other.UdpEndpoint?.Address.Equals(remoteIp) == true)
                        ClearStationConnectionState(other, "endpoint moved to another station", closeTcpClient: true);
                }

                if (ds.UdpEndpoint?.Address.Equals(remoteIp) != true)
                    ClearStationConnectionState(ds, "station reconnected from a new endpoint", closeTcpClient: true);

                ds.TcpClient   = tcpClient;
                ds.UdpEndpoint = endpoint;
                ds.ValidatedEndpoint = endpoint;
                ds.TeamNumber  = teamNumber;
                ds.IsNewDs     = isNewDs;
                ds.UdpSendPort = udpSendPort;
                ownsStation = true;
            }

            _logger.LogInformation("[{DsType}] Team {Team} connected in station {Station} ({IP}:{Port}).",
                isNewDs ? "2027 DS" : "NI DS", teamNumber, ds.Station, remoteIp, udpSendPort);

            // Send current game data if already set (e.g., DS reconnects after auto).
            // Note: 2027 DS receives game data in UDP control packets (Tag 32), not over TCP.
            if (!ds.IsNewDs && IsDriverStationCommunicationEnabled() && _arena.GameData.Length > 0)
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
            if (ds is not null && ownsStation)
            {
                ClearStationConnectionState(ds, "TCP disconnected", closeTcpClient: false);
                _logger.LogInformation("[{DsType}] DS {Station} (team {Team}) TCP disconnected.",
                    ds.IsNewDs ? "2027 DS" : "NI DS", ds.Station, ds.TeamNumber);
            }
            tcpClient.Dispose();
        }
    }

    private static async Task RunTcpReadLoopAsync(
        DriverStationConnection ds, NetworkStream stream, CancellationToken ct)
    {
        // TCP packets: 2-byte big-endian length prefix followed by payload.
        //   Payload[0] = packet type
        //     0x15 = Usage report
        //     0x16 = Robot log data
        //     0x17 = Error/event data
        //     0x1B = Challenge response
        //     0x1C = DS ping heartbeat
        //   PossumFMS currently ignores all inbound TCP payloads after the team-number tag.
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
            // All currently known packet types are intentionally ignored for now.
        }
    }

    // ── TCP game data ──────────────────────────────────────────────────────────

    private void OnGameDataChanged(string data)
    {
        if (!IsDriverStationCommunicationEnabled())
            return;

        foreach (var station in Stations.Keys)
        {
            var ds = Stations[station];
            if (ds.IsNewDs)
                continue; // 2027 DS receives game data in UDP control packets (Tag 32)

            _ = SendGameDataAsync(station, data, _ct);
        }
    }

    /// <summary>
    /// Sends game-specific data to a driver station over TCP (type 28).
    /// Used for legacy NI Driver Stations. The 2027 Driver Station receives game data
    /// embedded directly in the high-frequency UDP control packet.
    /// </summary>
    public async Task SendGameDataAsync(AllianceStation station, string gameData, CancellationToken ct = default)
    {
        if (!IsDriverStationCommunicationEnabled())
            return;

        var ds = Stations[station];
        if (ds.IsNewDs)
            return; // 2027 DS receives game data via UDP control packet (Tag 32)

        if (ds.TcpClient?.GetStream() is not { } stream) return;

        byte[] payload = System.Text.Encoding.UTF8.GetBytes(gameData);
        var    packet  = new byte[payload.Length + 4];
        packet[0] = 0x00;
        packet[1] = (byte)(payload.Length + 2); // size = type byte + data-length byte + data
        packet[2] = LegacyDsGameDataTag;        // packet type 28 (0x1C): game data
        packet[3] = (byte)payload.Length;
        payload.CopyTo(packet, 4);

        try
        {
            await stream.WriteAsync(packet, ct);
            _logger.LogDebug("[NI DS] Sent TCP game data '{GameData}' to Team {Team} in station {Station}.",
                gameData, ds.TeamNumber, station);
        }
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

    private void SetControlStarting()
    {
        lock (_controlHealthLock)
        {
            if (_controlHealth.State != DriverStationControlState.Faulted)
            {
                _controlHealth = new DriverStationControlHealth(
                    DriverStationControlState.Starting,
                    TransportAvailable: false,
                    LastFaultUtc: null,
                    LastFault: null);
            }
        }
    }

    private void SetTransportAvailable()
    {
        lock (_controlHealthLock)
        {
            _controlHealth = _controlHealth.State == DriverStationControlState.Faulted
                ? _controlHealth with { TransportAvailable = true }
                : new DriverStationControlHealth(
                    DriverStationControlState.Operational,
                    TransportAvailable: true,
                    LastFaultUtc: null,
                    LastFault: null);
        }
    }

    private void SetControlStopped()
    {
        lock (_controlHealthLock)
        {
            _controlHealth = new DriverStationControlHealth(
                DriverStationControlState.Stopped,
                TransportAvailable: false,
                LastFaultUtc: _controlHealth.LastFaultUtc,
                LastFault: _controlHealth.LastFault);
        }
    }

    private void HandleControlFault(Exception ex)
    {
        lock (_controlHealthLock)
        {
            _controlHealth = new DriverStationControlHealth(
                DriverStationControlState.Faulted,
                TransportAvailable: false,
                LastFaultUtc: DateTime.UtcNow,
                LastFault: ex.Message);
        }

        _logger.LogCritical(ex, "Driver Station UDP control channel faulted; asserting arena e-stop.");
        _arena.TriggerArenaEstop();
    }

    internal bool IsDriverStationCommunicationEnabled() => !_arena.FreePracticeEnabled;

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

    private void ClearStationConnectionState(
        DriverStationConnection station,
        string reason,
        bool closeTcpClient)
    {
        TcpClient? tcpClientToClose;

        lock (_stationStateLock)
        {
            tcpClientToClose = station.TcpClient;
            station.TcpClient   = null;
            station.UdpEndpoint = null;
            station.ValidatedEndpoint = null;
            station.IsNewDs     = false;
            station.UdpSendPort = DsUdpReceivePort;
            station.DsLinked    = false;
            station.RobotLinked = false;
            station.RadioLinked = false;
            station.RioLinked   = false;
            station.BatteryVoltage = 0;
        }

        if (closeTcpClient)
            tcpClientToClose?.Dispose();

        _logger.LogInformation(
            "Cleared DS station state for {Station} (team {Team}) because {Reason}.",
            station.Station,
            station.TeamNumber,
            reason);
    }
}
