using Microsoft.AspNetCore.SignalR;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using PossumFMS.Core.Network;

namespace PossumFMS.Core.Frontend;

/// <summary>
/// SignalR hub for the FMS frontend website. Clients subscribe to receive
/// real-time match state updates and send match control / team assignment commands.
///
/// Connect from the frontend:
///   const conn = new signalR.HubConnectionBuilder().withUrl("/fmshub").build();
/// </summary>
public sealed class FmsHub(
    Arena.Arena arena,
    GameLogic gameLogic,
    DriverStationManager dsManager,
    AccessPointManager apManager,
    MatchStateBroadcaster broadcaster,
    ILogger<FmsHub> logger) : Hub
{
    // ── Team assignment ────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a team to a station.
    /// Pass teamNumber = 0 to clear the slot.
    /// </summary>
    public async Task AssignTeam(int stationIndex, int teamNumber, string wpaKey = "")
    {
        var station = AllianceStations.All[stationIndex];

        if (teamNumber < 0)
            throw new HubException("Team number cannot be negative.");

        if (teamNumber != 0)
        {
            var duplicateStation = AllianceStations.All
                .Where(s => s != station)
                .FirstOrDefault(s => dsManager[s].TeamNumber == teamNumber);

            if (duplicateStation is not null)
                throw new HubException(
                    $"Team {teamNumber} is already assigned to {duplicateStation}. " +
                    "Use AssignTeams to move teams between stations in one operation.");
        }

        logger.LogInformation("Assigning team {Team} to {Station}.", teamNumber, station);
        dsManager.AssignTeam(station, teamNumber, wpaKey);
        await BroadcastMatchState();
    }

    /// <summary>
    /// Atomically assigns all six stations in order (Red1, Red2, Red3, Blue1, Blue2, Blue3).
    /// Use teamNumber = 0 to clear a slot.
    /// </summary>
    public async Task AssignTeams(IReadOnlyList<TeamAssignmentRequest> assignments)
    {
        if (assignments is null)
            throw new HubException("Assignments payload is required.");

        if (assignments.Count != AllianceStations.All.Count)
            throw new HubException($"Expected {AllianceStations.All.Count} assignments, received {assignments.Count}.");

        var duplicateTeams = assignments
            .Select((assignment, index) => new { assignment, index })
            .Where(x => x.assignment.TeamNumber > 0)
            .GroupBy(x => x.assignment.TeamNumber)
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                Team = g.Key,
                Stations = string.Join(", ", g.Select(x => AllianceStations.All[x.index].ToString()).OrderBy(s => s))
            })
            .ToList();

        if (duplicateTeams.Count > 0)
        {
            var details = string.Join("; ", duplicateTeams.Select(x => $"Team {x.Team}: {x.Stations}"));
            throw new HubException($"Duplicate team assignments are not allowed. {details}");
        }

        for (int i = 0; i < AllianceStations.All.Count; i++)
        {
            if (assignments[i].TeamNumber < 0)
                throw new HubException($"Team number for {AllianceStations.All[i]} cannot be negative.");
        }

        logger.LogInformation("Assigning all stations at once requested by {Client}.", Context.ConnectionId);

        for (int i = 0; i < AllianceStations.All.Count; i++)
            dsManager.AssignTeam(AllianceStations.All[i], 0, string.Empty);

        for (int i = 0; i < AllianceStations.All.Count; i++)
            dsManager.AssignTeam(AllianceStations.All[i], assignments[i].TeamNumber, assignments[i].WpaKey ?? string.Empty);

        await BroadcastMatchState();
    }

    public async Task ConfigureAccessPoint()
    {
        logger.LogInformation("Manual AP configure requested by {Client}.", Context.ConnectionId);
        await apManager.ConfigureNowAsync();
        await BroadcastMatchState();
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    public async Task AdjustFuelPoints(string alliance, bool isAuto, int delta)
    {
        var allianceColor = ParseAllianceColor(alliance);
        logger.LogInformation(
            "Manual fuel adjustment by {Client}: Alliance={Alliance}, IsAuto={IsAuto}, Delta={Delta}.",
            Context.ConnectionId,
            allianceColor,
            isAuto,
            delta);

        gameLogic.AdjustFuelPoints(allianceColor, isAuto, delta);
        await BroadcastMatchState();
    }

    public async Task SetAutoTowerClimb(int stationIndex, bool climbed)
    {
        var station = GetStationByIndex(stationIndex);
        logger.LogInformation(
            "Set auto tower climb by {Client}: Station={Station}, Climbed={Climbed}.",
            Context.ConnectionId,
            station,
            climbed);

        gameLogic.SetAutoTowerClimbed(station, climbed);
        await BroadcastMatchState();
    }

    public async Task SetEndgameTowerLevel(int stationIndex, int level)
    {
        var station = GetStationByIndex(stationIndex);
        if (!Enum.IsDefined(typeof(TowerEndgameLevel), level))
            throw new HubException($"Invalid tower level '{level}'.");

        var towerLevel = (TowerEndgameLevel)level;
        logger.LogInformation(
            "Set endgame tower level by {Client}: Station={Station}, Level={Level}.",
            Context.ConnectionId,
            station,
            towerLevel);

        gameLogic.SetEndgameTowerLevel(station, towerLevel);
        await BroadcastMatchState();
    }

    // ── Match control ──────────────────────────────────────────────────────────

    public async Task StartPreMatch()
    {
        logger.LogInformation("StartPreMatch requested by {Client}.", Context.ConnectionId);
        arena.StartPreMatch();
        await BroadcastMatchState();
    }

    public async Task StartMatch()
    {
        logger.LogInformation("StartMatch requested by {Client}.", Context.ConnectionId);
        // Only start if every non-bypassed station has a driver station linked.
        if (!dsManager.Stations.Values.All(s => s.IsReady))
        {
            logger.LogWarning("StartMatch blocked — not all stations ready.");
            return;
        }
        arena.StartMatch();
        await BroadcastMatchState();
    }

    public async Task AbortMatch()
    {
        logger.LogInformation("AbortMatch requested by {Client}.", Context.ConnectionId);
        arena.AbortMatch();
        await BroadcastMatchState();
    }

    public async Task ClearMatch()
    {
        logger.LogInformation("ClearMatch requested by {Client}.", Context.ConnectionId);
        dsManager.ResetAllStops();
        arena.ResetArenaEstop();
        arena.ClearMatch();
        await BroadcastMatchState();
    }

    public async Task TriggerArenaEstop()
    {
        logger.LogWarning("ArenaEstop triggered by {Client}.", Context.ConnectionId);
        arena.TriggerArenaEstop();
        await BroadcastMatchState();
    }

    public async Task ResetArenaEstop()
    {
        arena.ResetArenaEstop();
        await BroadcastMatchState();
    }

    // Station-level stops (stationIndex: 0=Red1 … 5=Blue3)
    public async Task EstopStation(int stationIndex)
    {
        var station = AllianceStations.All[stationIndex];
        logger.LogWarning("E-stop {Station} by {Client}.", station, Context.ConnectionId);
        dsManager.Estop(station);
        await BroadcastMatchState();
    }

    public async Task AstopStation(int stationIndex)
    {
        var station = AllianceStations.All[stationIndex];
        logger.LogWarning("A-stop {Station} by {Client}.", station, Context.ConnectionId);
        dsManager.Astop(station);
        await BroadcastMatchState();
    }

    public async Task BypassStation(int stationIndex, bool bypassed)
    {
        var station = AllianceStations.All[stationIndex];
        logger.LogInformation("{Action} bypass on {Station} by {Client}.",
            bypassed ? "Set" : "Clear", station, Context.ConnectionId);
        dsManager.SetBypass(station, bypassed);
        await BroadcastMatchState();
    }

    // ── State push ─────────────────────────────────────────────────────────────

    public Task RequestMatchState() => BroadcastMatchState();

    private Task BroadcastMatchState() => broadcaster.BroadcastAsync();

    private static AllianceStation GetStationByIndex(int stationIndex)
    {
        if (stationIndex < 0 || stationIndex >= AllianceStations.All.Count)
            throw new HubException($"stationIndex must be between 0 and {AllianceStations.All.Count - 1}.");

        return AllianceStations.All[stationIndex];
    }

    private static AllianceColor ParseAllianceColor(string alliance)
    {
        if (Enum.TryParse<AllianceColor>(alliance, true, out var parsed))
            return parsed;

        throw new HubException("alliance must be either 'Red' or 'Blue'.");
    }

    public sealed record TeamAssignmentRequest(int TeamNumber, string WpaKey = "");
}
