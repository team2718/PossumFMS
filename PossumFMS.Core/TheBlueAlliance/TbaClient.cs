using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PossumFMS.Core.Database;

namespace PossumFMS.Core.TheBlueAlliance;

/// <summary>
/// HTTP client for The Blue Alliance v3 API.
/// Use <see cref="ImportTeamsWithAvatarsAsync"/> to fetch all teams for an event
/// and their avatars in parallel.
/// The named HttpClient "TBA" must be registered in DI with the base address
/// and X-TBA-Auth-Key header pre-configured.
/// </summary>
public sealed class TbaClient(IHttpClientFactory httpClientFactory, ILogger<TbaClient> logger)
{
    private const string ClientName = "TBA";

    /// <summary>
    /// Fetches all teams at the given event and their current-year avatar images,
    /// then returns the full list. Avatars are fetched in parallel (max 10 at a time)
    /// to keep total time reasonable. Teams without avatars will have
    /// <see cref="TeamRecord.AvatarBase64"/> = null.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown (with a user-readable message) on auth failure (401) or unknown
    /// event key (404). Callers should surface this as a HubException.
    /// </exception>
    public async Task<List<TeamRecord>> ImportTeamsWithAvatarsAsync(
        string eventKey,
        CancellationToken cancellationToken = default)
    {
        var teams = await GetEventTeamsAsync(eventKey, cancellationToken);

        logger.LogInformation(
            "Fetched {Count} teams from event {EventKey}. Fetching avatars…",
            teams.Count, eventKey);

        var year = DateTime.UtcNow.Year;
        var semaphore = new SemaphoreSlim(10, 10);

        var tasks = teams.Select(async team =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var avatar = await GetTeamAvatarBase64Async($"frc{team.TeamNumber}", year, cancellationToken);
                return team with { AvatarBase64 = avatar };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        logger.LogInformation(
            "Avatar fetch complete for event {EventKey}. {WithAvatar}/{Total} teams have avatars.",
            eventKey,
            results.Count(t => t.AvatarBase64 is not null),
            results.Length);

        return [.. results];
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<List<TeamRecord>> GetEventTeamsAsync(
        string eventKey,
        CancellationToken cancellationToken)
    {
        using var response = await httpClientFactory
            .CreateClient(ClientName)
            .GetAsync($"event/{eventKey}/teams", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException(
                "The Blue Alliance rejected the API key. " +
                "Check the TheBlueAlliance:ApiKey setting in appsettings.json.");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                $"Event '{eventKey}' was not found on The Blue Alliance. " +
                "Verify the event key (e.g. 2026nyny).");

        response.EnsureSuccessStatusCode();

        var tbaTeams = await response.Content
            .ReadFromJsonAsync<List<TbaTeam>>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The Blue Alliance returned an empty team list.");

        return tbaTeams.Select(t => new TeamRecord
        {
            TeamNumber = t.TeamNumber,
            Nickname   = t.Nickname   ?? "",
            Name       = t.Name       ?? "",
            City       = t.City       ?? "",
            StateProv  = t.StateProv  ?? "",
            Country    = t.Country    ?? "",
            SchoolName = t.SchoolName ?? "",
            RookieYear = t.RookieYear,
        }).ToList();
    }

    private async Task<string?> GetTeamAvatarBase64Async(
        string teamKey,
        int year,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClientFactory
                .CreateClient(ClientName)
                .GetAsync($"team/{teamKey}/media/{year}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var mediaItems = await response.Content
                .ReadFromJsonAsync<List<TbaMedia>>(cancellationToken: cancellationToken);

            return mediaItems
                ?.FirstOrDefault(m => m.Type == "avatar")
                ?.Details?.Base64Image;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch avatar for {TeamKey}.", teamKey);
            return null;
        }
    }

    // ── TBA JSON contracts ────────────────────────────────────────────────────

    private sealed class TbaTeam
    {
        [JsonPropertyName("team_number")] public int    TeamNumber { get; init; }
        [JsonPropertyName("nickname")]    public string? Nickname   { get; init; }
        [JsonPropertyName("name")]        public string? Name       { get; init; }
        [JsonPropertyName("city")]        public string? City       { get; init; }
        [JsonPropertyName("state_prov")]  public string? StateProv  { get; init; }
        [JsonPropertyName("country")]     public string? Country    { get; init; }
        [JsonPropertyName("school_name")] public string? SchoolName { get; init; }
        [JsonPropertyName("rookie_year")] public int     RookieYear { get; init; }
    }

    private sealed class TbaMedia
    {
        [JsonPropertyName("type")]    public string?          Type    { get; init; }
        [JsonPropertyName("details")] public TbaMediaDetails? Details { get; init; }
    }

    private sealed class TbaMediaDetails
    {
        [JsonPropertyName("base64Image")] public string? Base64Image { get; init; }
    }
}
