namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// A listing's holder mix over its most recent quarters, oldest first, or the reason there is
/// none to show.
/// </summary>
public sealed class HolderMixHistory
{
    // The listed ticker read: the requested ticker's exact listing, or the stock's primary one.
    public string ListedTicker { get; init; }

    // True when the listing is read on its own (an ETF series or a sibling share class).
    public bool ExactListing { get; init; }

    public IReadOnlyList<HolderMixQuarter> Quarters { get; init; } = [];

    // The quarter whose positions the combined view carries for filers yet to file, if any.
    public DateOnly? CarriedFrom { get; init; }

    // Why no quarter is shown; null when Quarters holds at least one.
    public string Unavailable { get; init; }
}
