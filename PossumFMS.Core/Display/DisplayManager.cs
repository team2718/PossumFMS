using PossumFMS.Core.Database;

namespace PossumFMS.Core.Display;

/// <summary>
/// Tracks which view the audience overlay is currently showing, alliance order,
/// Carter audio settings, and holds the last committed match result for display. Thread-safe;
/// all mutations go through the FmsHub and are immediately reflected in the next
/// MatchState broadcast.
/// </summary>
public sealed class DisplayManager
{
    private readonly object _lock = new();
    private readonly DatabaseService? _databaseService;

    public string AudienceView { get; private set; } = "live";
    public string AllianceOrder { get; private set; } = "redLeft";
    public bool CarterAudio { get; private set; }
    public MatchResultRecord? LastCommittedMatch { get; private set; }

    public DisplayManager(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        CarterAudio = databaseService.GetCarterAudio();
    }

    public DisplayManager()
    {
    }

    private static readonly HashSet<string> KnownViews =
    [
        "blank",
        "live",
        "matchResults",
    ];

    private static readonly HashSet<string> KnownOrders = ["blueLeft", "redLeft"];

    /// <summary>
    /// Sets the audience overlay view. Throws ArgumentException for unknown views
    /// so callers can surface it as a HubException.
    /// </summary>
    public void SetAudienceView(string view)
    {
        if (!KnownViews.Contains(view))
            throw new ArgumentException($"Unknown audience view '{view}'. Known views: {string.Join(", ", KnownViews)}.");

        lock (_lock) AudienceView = view;
    }

    /// <summary>
    /// Sets the alliance display order. Valid values: "blueLeft", "redLeft".
    /// </summary>
    public void SetAllianceOrder(string order)
    {
        if (!KnownOrders.Contains(order))
            throw new ArgumentException($"Unknown alliance order '{order}'. Valid: blueLeft, redLeft.");

        lock (_lock) AllianceOrder = order;
    }

    /// <summary>
    /// Sets whether alternate Carter sound effects should be played on audience displays.
    /// </summary>
    public void SetCarterAudio(bool enabled)
    {
        lock (_lock)
        {
            CarterAudio = enabled;
            _databaseService?.SaveCarterAudio(enabled);
        }
    }

    public void SetLastCommittedMatch(MatchResultRecord match)
    {
        lock (_lock) LastCommittedMatch = match;
    }
}
