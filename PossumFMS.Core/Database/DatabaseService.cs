using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace PossumFMS.Core.Database;

/// <summary>
/// Simple JSON file-based persistence layer. All data is loaded into memory at
/// startup and written to individual files on mutation. All reads are served
/// from the in-memory cache.
/// </summary>
public sealed class DatabaseService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataPath;
    private readonly string _matchesPath;
    private readonly string _settingsPath;
    private readonly ILogger<DatabaseService> _logger;
    private readonly object _lock = new();

    private Dictionary<int, TeamRecord> _teamCache = [];
    private readonly List<MatchResultRecord> _matchCache = [];
    private SystemSettings _settings = new();

    public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger, IHostEnvironment? hostEnvironment = null)
    {
        _logger = logger;
        var rawPath = configuration["Database:DataPath"] ?? "./data";
        _dataPath = Path.IsPathRooted(rawPath) || hostEnvironment is null
            ? rawPath
            : Path.Combine(hostEnvironment.ContentRootPath, rawPath);

        _matchesPath = Path.Combine(_dataPath, "matches");
        _settingsPath = Path.Combine(_dataPath, "settings.json");

        Directory.CreateDirectory(_dataPath);
        Directory.CreateDirectory(_matchesPath);

        LoadTeams();
        LoadMatches();
        LoadSettings();
    }

    /// <summary>Returns a snapshot copy of the in-memory team cache.</summary>
    public Dictionary<int, TeamRecord> GetTeams()
    {
        lock (_lock) return new Dictionary<int, TeamRecord>(_teamCache);
    }

    /// <summary>Returns a snapshot copy of all committed match results.</summary>
    public List<MatchResultRecord> GetMatchResults()
    {
        lock (_lock) return new List<MatchResultRecord>(_matchCache);
    }

    /// <summary>Returns whether Carter audio alternates are enabled.</summary>
    public bool GetCarterAudio()
    {
        lock (_lock) return _settings.CarterAudio;
    }

    /// <summary>
    /// Atomically persists the Carter audio setting to settings.json.
    /// </summary>
    public void SaveCarterAudio(bool enabled)
    {
        var tempPath = _settingsPath + ".tmp";

        lock (_lock)
        {
            _settings.CarterAudio = enabled;
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_settings, JsonOptions));
            File.Move(tempPath, _settingsPath, overwrite: true);
        }

        _logger.LogInformation("Saved CarterAudio={Enabled} to {Path}.", enabled, _settingsPath);
    }

    /// <summary>
    /// Atomically writes the complete team list to teams.json and replaces the
    /// in-memory cache. The write uses a temp file + rename so a crash mid-write
    /// does not corrupt the existing data.
    /// </summary>
    public void SaveTeams(IList<TeamRecord> teams)
    {
        var path = Path.Combine(_dataPath, "teams.json");
        var tempPath = path + ".tmp";

        lock (_lock)
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(teams, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
            _teamCache = teams.ToDictionary(t => t.TeamNumber);
        }

        _logger.LogInformation("Saved {Count} teams to {Path}.", teams.Count, path);
    }

    /// <summary>
    /// Writes a single match result to its own JSON file and appends it to the
    /// in-memory cache.
    /// </summary>
    public void SaveMatchResult(MatchResultRecord result)
    {
        var filename = $"match_{result.MatchType}_{result.MatchNumber:D3}_{result.CommittedAt:yyyyMMdd_HHmmss}.json";
        var path = Path.Combine(_matchesPath, filename);

        lock (_lock)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
            _matchCache.Add(result);
        }

        _logger.LogInformation("Saved match result to {Path}.", path);
    }

    // ── Private boot loaders ──────────────────────────────────────────────────

    private void LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            _logger.LogInformation("No settings.json found at {Path}; starting with default settings.", _settingsPath);
            return;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<SystemSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            if (settings is not null)
            {
                _settings = settings;
                _logger.LogInformation("Loaded settings from {Path}. CarterAudio: {CarterAudio}.", _settingsPath, _settings.CarterAudio);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from {Path}; starting with default settings.", _settingsPath);
        }
    }

    private void LoadTeams()
    {
        var path = Path.Combine(_dataPath, "teams.json");
        if (!File.Exists(path))
        {
            _logger.LogInformation("No teams.json found at {Path}; starting with empty team database.", path);
            return;
        }

        try
        {
            var teams = JsonSerializer.Deserialize<List<TeamRecord>>(File.ReadAllText(path), JsonOptions);
            if (teams is not null)
            {
                _teamCache = teams.ToDictionary(t => t.TeamNumber);
                _logger.LogInformation("Loaded {Count} teams from {Path}.", teams.Count, path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load teams from {Path}; starting with empty team database.", path);
        }
    }

    private void LoadMatches()
    {
        try
        {
            var files = Directory.GetFiles(_matchesPath, "match_*.json").OrderBy(f => f).ToArray();
            var loaded = 0;

            foreach (var filePath in files)
            {
                try
                {
                    var result = JsonSerializer.Deserialize<MatchResultRecord>(
                        File.ReadAllText(filePath), JsonOptions);

                    if (result is not null)
                    {
                        _matchCache.Add(result);
                        loaded++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping malformed match file {Path}.", filePath);
                }
            }

            _logger.LogInformation("Loaded {Count} match results from {Path}.", loaded, _matchesPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate match files in {Path}.", _matchesPath);
        }
    }
}
