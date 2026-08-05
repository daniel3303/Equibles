using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.InsiderTrading.BusinessLogic.Models;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Builds the <see cref="DailyBarContext"/> a price evaluation runs against: the stored bar
/// plus the split factor between the transaction date and the series' present basis. One
/// implementation for all three persisting call sites (ingest, backfill, reprocess) so they
/// can never disagree on the basis rules.
/// </summary>
public static class InsiderDailyBars
{
    /// <summary>
    /// Insider rows are issuer-level and priced against the primary series, so the resolver
    /// runs with <c>listedTicker = null</c>: unattributed splits count, sibling-attributed
    /// splits are skipped, and an unattributable one marks the basis ambiguous (pending)
    /// rather than guessed at.
    /// </summary>
    public static DailyBarContext Build(
        decimal? close,
        decimal? low,
        decimal? high,
        DateOnly transactionDate,
        IReadOnlyList<StockSplit> splits,
        string primaryTicker,
        IReadOnlyCollection<string> secondaryTickers
    )
    {
        var ambiguous = !SplitBasisResolver.TryResolveFactor(
            transactionDate,
            splits,
            listedTicker: null,
            primaryTicker,
            secondaryTickers,
            out var factor
        );
        return new DailyBarContext
        {
            Close = close,
            Low = low,
            High = high,
            SplitFactorToPresent = factor,
            SplitBasisAmbiguous = ambiguous,
        };
    }
}
