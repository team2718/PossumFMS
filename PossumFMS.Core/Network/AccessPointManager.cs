using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;

namespace PossumFMS.Core.Network;

/// <summary>
/// Manages configuration and monitoring of the VH-113 access point over its REST API.
///
/// Configuration flow:
///   1. When any station's team assignment changes, a debounced POST to /configuration
///      is sent with the SSID (team number) and WPA key for each occupied slot.
///   2. A background loop polls GET /status every second; if the AP reports ACTIVE
///      but its SSIDs don't match what was last configured, the configuration is retried
///      automatically (the same self-healing logic as cheesy-arena).
///
/// API:
///   POST /configuration  — { channel, stationConfigurations: { red1: { ssid, wpaKey }, … } }
///   GET  /status         — { channel, status, stationStatuses: { red1: { ssid, isLinked, … }, … } }
/// </summary>
public sealed class AccessPointManager : BackgroundService
{
    // ── Config ─────────────────────────────────────────────────────────────────

    private const int PollIntervalMs    = 1000;
    private const int DebounceMs        =   50;
    private const int HttpTimeoutSec    =    3;

    // Station keys in AllianceStations.All order: R1, R2, R3, B1, B2, B3.
    private static readonly string[] StationKeys = ["red1", "red2", "red3", "blue1", "blue2", "blue3"];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── State ──────────────────────────────────────────────────────────────────

    private readonly DriverStationManager _dsManager;
    private readonly ILogger<AccessPointManager> _logger;
    private readonly HttpClient _http;
    private readonly int _channel;

    /// <summary>Per-station WiFi status, indexed to match AllianceStations.All order.</summary>
    public WifiStationStatus[] StationStatuses { get; } = Enumerable.Range(0, 6)
        .Select(_ => new WifiStationStatus()).ToArray();

    public string ApStatus { get; private set; } = "UNKNOWN";

    // Last team numbers successfully sent to the AP (0 = empty slot).
    private readonly int[] _lastConfiguredTeams = new int[6];

    // Debounce: cancels the previous pending configure task on each new change.
    private CancellationTokenSource? _debounceCts;
    private readonly object           _debounceLock = new();

    private readonly SemaphoreSlim _configureLock = new(1, 1);

    public AccessPointManager(
        DriverStationManager dsManager,
        IConfiguration config,
        ILogger<AccessPointManager> logger)
    {
        _dsManager = dsManager;
        _logger    = logger;

        var address  = config["AccessPoint:Address"] ?? "10.0.100.2";
        var password = config["AccessPoint:Password"] ?? "";
        _channel     = config.GetValue("AccessPoint:Channel", 36);

        _http = new HttpClient { BaseAddress = new Uri($"http://{address}") };
        if (!string.IsNullOrEmpty(password))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", password);

        _dsManager.TeamAssignmentsChanged += ScheduleReconfiguration;
    }

    // ── BackgroundService ──────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("AccessPoint manager started (target: {BaseUrl}).", _http.BaseAddress);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMs, ct);
                await UpdateMonitoringAsync(ct);

                // Self-heal: re-send config if the AP is ACTIVE but SSIDs don't match.
                if (ApStatus == "ACTIVE" && !StatusMatchesLastConfigured())
                {
                    _logger.LogWarning("AP is ACTIVE but station config doesn't match — retrying.");
                    await ConfigureAsync(ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("AP monitoring error: {Error}", ex.Message);
            }
        }
    }

    // ── Reconfiguration trigger ────────────────────────────────────────────────

    /// <summary>
    /// Schedules a POST /configuration after a short debounce delay so that rapid
    /// successive team-number changes (e.g. assigning all 6 stations at once) are
    /// collapsed into a single API call.
    /// </summary>
    private void ScheduleReconfiguration()
    {
        CancellationTokenSource newCts;
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts = newCts = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceMs, newCts.Token);
                await ConfigureAsync(newCts.Token);
            }
            catch (OperationCanceledException) { }
        });
    }

    // ── POST /configuration ────────────────────────────────────────────────────

    private async Task ConfigureAsync(CancellationToken ct)
    {
        await _configureLock.WaitAsync(ct);
        try
        {
            var stationConfigs = new Dictionary<string, StationConfigRequest>();
            for (int i = 0; i < 6; i++)
            {
                var ds = _dsManager.Stations[AllianceStations.All[i]];
                _lastConfiguredTeams[i] = ds.TeamNumber;
                if (ds.TeamNumber == 0) continue; // Empty slot — omit from request.

                stationConfigs[StationKeys[i]] = new StationConfigRequest
                {
                    Ssid   = ds.TeamNumber.ToString(),
                    WpaKey = ds.WpaKey,
                };
            }

            var body    = JsonSerializer.Serialize(new ConfigRequest(_channel, stationConfigs), JsonOpts);
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            ApStatus = "CONFIGURING";

            using var cts  = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(HttpTimeoutSec));

            using var resp = await _http.PostAsync("/configuration", content, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("AP configuration failed ({Code}): {Body}", resp.StatusCode, detail);
                return;
            }

            _logger.LogInformation(
                "AP configuration accepted — channel {Ch}, {Count} station(s) configured.",
                _channel, stationConfigs.Count);
        }
        finally
        {
            _configureLock.Release();
        }
    }

    // ── GET /status ───────────────────────────────────────────────────────────

    private async Task UpdateMonitoringAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(HttpTimeoutSec));

        HttpResponseMessage resp;
        try
        {
            resp = await _http.GetAsync("/status", cts.Token);
        }
        catch (Exception ex)
        {
            if (ApStatus != "ERROR")
            {
                ApStatus = "ERROR";
                _logger.LogWarning("AP unreachable: {Error}", ex.Message);
            }
            return;
        }

        if (!resp.IsSuccessStatusCode)
        {
            ApStatus = "ERROR";
            _logger.LogWarning("AP /status returned {Code}.", resp.StatusCode);
            return;
        }

        var apStatus = await JsonSerializer.DeserializeAsync<ApStatusResponse>(
            await resp.Content.ReadAsStreamAsync(ct), JsonOpts, ct);

        if (apStatus is null) return;

        if (ApStatus != apStatus.Status)
        {
            _logger.LogInformation("AP status: {Old} → {New}", ApStatus, apStatus.Status);
            ApStatus = apStatus.Status;
        }

        for (int i = 0; i < 6; i++)
        {
            apStatus.StationStatuses.TryGetValue(StationKeys[i], out var ss);
            UpdateStationStatus(StationStatuses[i], ss);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool StatusMatchesLastConfigured()
    {
        for (int i = 0; i < 6; i++)
            if (StationStatuses[i].TeamId != _lastConfiguredTeams[i])
                return false;
        return true;
    }

    private static void UpdateStationStatus(WifiStationStatus ws, StationStatusResponse? ss)
    {
        if (ss is null)
        {
            ws.TeamId            = 0;
            ws.RadioLinked       = false;
            ws.BandwidthUsedMbps = 0;
            ws.RxRateMbps        = 0;
            ws.TxRateMbps        = 0;
            ws.SignalNoiseRatio  = 0;
            ws.ConnectionQuality = 0;
            return;
        }

        _ = int.TryParse(ss.Ssid, out int teamId);
        ws.TeamId            = teamId;
        ws.RadioLinked       = ss.IsLinked;
        ws.BandwidthUsedMbps = ss.BandwidthUsedMbps;
        ws.RxRateMbps        = ss.RxRateMbps;
        ws.TxRateMbps        = ss.TxRateMbps;
        ws.SignalNoiseRatio  = ss.SignalNoiseRatio;
        ws.ConnectionQuality = ss.ConnectionQuality switch
        {
            "caution"   => 1,
            "warning"   => 2,
            "good"      => 3,
            "excellent" => 4,
            _           => 0,
        };
    }

    // ── JSON models ────────────────────────────────────────────────────────────

    private sealed record ConfigRequest(
        [property: JsonPropertyName("channel")]               int Channel,
        [property: JsonPropertyName("stationConfigurations")] Dictionary<string, StationConfigRequest> StationConfigurations);

    private sealed class StationConfigRequest
    {
        [JsonPropertyName("ssid")]   public string Ssid   { get; init; } = "";
        [JsonPropertyName("wpaKey")] public string WpaKey { get; init; } = "";
    }

    private sealed class ApStatusResponse
    {
        [JsonPropertyName("channel")]         public int                                      Channel         { get; init; }
        [JsonPropertyName("status")]          public string                                   Status          { get; init; } = "";
        [JsonPropertyName("stationStatuses")] public Dictionary<string, StationStatusResponse?> StationStatuses { get; init; } = [];
    }

    private sealed class StationStatusResponse
    {
        [JsonPropertyName("ssid")]             public string  Ssid             { get; init; } = "";
        [JsonPropertyName("isLinked")]         public bool    IsLinked         { get; init; }
        [JsonPropertyName("rxRateMbps")]       public double  RxRateMbps       { get; init; }
        [JsonPropertyName("txRateMbps")]       public double  TxRateMbps       { get; init; }
        [JsonPropertyName("signalNoiseRatio")] public int     SignalNoiseRatio  { get; init; }
        [JsonPropertyName("bandwidthUsedMbps")]public double  BandwidthUsedMbps{ get; init; }
        [JsonPropertyName("connectionQuality")]public string  ConnectionQuality { get; init; } = "";
    }

    public override void Dispose()
    {
        _dsManager.TeamAssignmentsChanged -= ScheduleReconfiguration;
        _http.Dispose();
        _debounceCts?.Dispose();
        _configureLock.Dispose();
        base.Dispose();
    }
}
