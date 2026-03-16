using Microsoft.AspNetCore.SignalR;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using PossumFMS.Core.Network;

namespace PossumFMS.Core.Frontend;

/// <summary>
/// Periodically pushes MatchState to all connected SignalR clients while a
/// match is active, so the timer and station indicators stay live without
/// requiring any button press from the operator.
///
/// FmsHub calls BroadcastAsync() directly for immediate updates (e.g. after
/// phase transitions or team assignments); this service supplements those
/// calls with a 500 ms heartbeat during Auto/Teleop so the countdown ticks
/// smoothly in the browser.
/// </summary>
public sealed class MatchStateBroadcaster(
    IHubContext<FmsHub> hubContext,
    Arena.Arena         arena,
    GameLogic            gameLogic,
    DriverStationManager dsManager,
    AccessPointManager   apManager) : BackgroundService
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Push the current state snapshot to all connected clients immediately.</summary>
    public Task BroadcastAsync() =>
        hubContext.Clients.All.SendAsync("MatchState", Build());

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(BroadcastInterval, ct);
            await BroadcastAsync();
        }
    }

    internal object Build()
    {
        var loopTiming = dsManager.GetLoopTimingSnapshot();

        return new
        {
        phase         = arena.Phase.ToString(),
        matchType     = arena.MatchType.ToString(),
        matchNumber   = arena.MatchNumber,
        timeRemaining = arena.TimeRemaining.TotalSeconds,
        arenaEstop    = arena.ArenaEstop,
        wasAborted    = arena.WasAborted,
        redScore      = gameLogic.RedScore.Total,
        blueScore     = gameLogic.BlueScore.Total,
        loopTiming    = new
        {
            currentMs = loopTiming.CurrentMs,
            maxMs30s  = loopTiming.MaxMs30s,
        },
        accessPoint   = new { status = apManager.ApStatus },
        stations      = AllianceStations.All.Select((s, i) =>
        {
            var ds   = dsManager[s];
            var wifi = apManager.StationStatuses[i];
            return new
            {
                index         = i,
                alliance      = s.Color.ToString(),
                position      = (int)s.Position,
                teamNumber    = ds.TeamNumber,
                dsLinked      = ds.DsLinked,
                robotLinked   = ds.RobotLinked,
                radioLinked   = ds.RadioLinked,
                rioLinked     = ds.RioLinked,
                battery       = ds.BatteryVoltage,
                tripTimeMs    = ds.DsRobotTripTimeMs,
                missedPackets = ds.MissedPacketCount,
                secondsSinceLastRobotLink = ds.SecondsSinceLastRobotLink,
                estop         = ds.Estop,
                astop         = ds.Astop,
                bypassed      = ds.Bypassed,
                wrongStation  = ds.WrongStation,
                wifi          = new
                {
                    radioLinked       = wifi.RadioLinked,
                    bandwidthMbps     = wifi.BandwidthUsedMbps,
                    rxRateMbps        = wifi.RxRateMbps,
                    txRateMbps        = wifi.TxRateMbps,
                    snr               = wifi.SignalNoiseRatio,
                    connectionQuality = wifi.ConnectionQuality,
                },
            };
        }),
    };
    }
}
