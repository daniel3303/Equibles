using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>
/// One listing's newest holder signals with the quarter before for trends, its overlap peers and
/// the filers holding it at the highest weight. <see cref="Unavailable"/> says why there is
/// nothing to show.
/// </summary>
public sealed record StockHolderSignalsView
{
    /// <summary>Null for the issuer's primary listing.</summary>
    public string ListedTicker { get; init; }

    public StockQuarterlyHolderSignals Current { get; init; }
    public StockQuarterlyHolderSignals Previous { get; init; }

    /// <summary>The quarter the peers were measured in; peers are kept for the newest quarters only.</summary>
    public DateOnly? PeersAsOf { get; init; }

    public IReadOnlyList<StockOverlapPeer> Peers { get; init; } = [];
    public IReadOnlyList<StockConvictionHolder> ConvictionHolders { get; init; } = [];

    /// <summary>The newest closed quarter computed with signals, whether or not it holds this listing.</summary>
    public DateOnly? LatestQuarter { get; init; }

    public string Unavailable { get; init; }
}
