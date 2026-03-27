using PossumFMS.Core.Database;

namespace PossumFMS.Core.Display;

/// <summary>
/// Tracks which view the audience overlay is currently showing and holds the
/// last committed match result for display. Thread-safe; all mutations go
/// through the FmsHub and are immediately reflected in the next MatchState
/// broadcast.
/// </summary>
public sealed class DisplayManager
{
    private readonly object _lock = new();

    public string AudienceView { get; private set; } = "live";
    public string AllianceOrder { get; private set; } = "redLeft";
    public MatchResultRecord? LastCommittedMatch { get; private set; }

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

    public void SetLastCommittedMatch(MatchResultRecord match)
    {
        lock (_lock) LastCommittedMatch = match;
    }
}
