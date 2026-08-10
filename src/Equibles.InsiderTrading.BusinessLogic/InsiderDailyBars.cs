using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.InsiderTrading.BusinessLogic.Models;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Builds the <see cref="DailyBarContext"/> a price evaluation runs against: the stored bar
/// plus the captured split factor since the transaction date. The factor does not prove the
/// provider-served raw bar's basis. One implementation serves all three persisting call sites
/// (ingest, backfill, reprocess) so they use the same attribution and pending-marker rules.
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
