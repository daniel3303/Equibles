using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Yahoo.Data.Prices;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.Yahoo.HostedService.Services;

// Share count and market cap are read on the feed's own cadence, while closes land from several
// writers every session. This pass compares the stored pair with the stored close and has no
// stamp of its own, so it covers every close writer and repeating it changes nothing.
[Service]
public class MarketCapitalizationRestatementService(
    EquityDailyStockPriceRepository prices,
    EquityListingRepository listings,
    EquitySecurityRepository securities,
    StockSplitRepository splits,
    IEnumerable<IVenueCapitalizationOwner> venueOwners,
    ILogger<MarketCapitalizationRestatementService> logger
)
{
    private const int BatchSize = 500;

    // A snapshot older than this is too far from the newest close to vouch for the share count.
    internal const int MaxSnapshotAgeDays = 90;

    /// <summary>Restates every presented listing's market cap on its newest traded close; returns how many changed.</summary>
    public async Task<int> Restate(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var oldestSnapshot = today.AddDays(-MaxSnapshotAgeDays);
        var due = (await prices.GetPresentedCapitalizations().ToListAsync(cancellationToken))
            .Where(pair =>
                pair.SnapshotTime != null
                && DateOnly.FromDateTime(pair.SnapshotTime.Value) >= oldestSnapshot
                && pair.NewestTradedClose is > 0
                && IsOffClose(pair)
            )
            .OrderBy(pair => pair.SnapshotTime)
            .ToList();

        var restated = 0;
        foreach (var batch in due.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            restated += await RestateBatch(batch, cancellationToken);
        }
        if (restated > 0)
            logger.LogInformation(
                "Restated {Restated} market caps on the newest traded close; {Held} pairs stand as stored",
                restated,
                due.Count - restated
            );
        return restated;
    }

    private static bool IsOffClose(PresentedCapitalization pair) =>
        Math.Abs(
            pair.SharesOutstanding
                * (double)pair.NewestTradedClose.Value
                * (double)(pair.QuoteUnitMultiplier ?? 1m)
                - pair.MarketCapitalization
        ) >= 1;

    private async Task<int> RestateBatch(
        PresentedCapitalization[] batch,
        CancellationToken cancellationToken
    )
    {
        var listingIds = batch.Select(pair => pair.EquityListingId).ToList();
        var issuerIds = batch.Select(pair => pair.EquityIssuerId).Distinct().ToList();
        var from = DateOnly
            .FromDateTime(batch.Min(pair => pair.SnapshotTime.Value))
            .AddDays(-MarketCapitalizationBasis.SnapshotLookbackDays);

        var barsByListing = (
            await prices
                .GetAllSeries()
                .Where(price =>
                    listingIds.Contains(price.EquityListingId)
                    && price.Date >= from
                    && price.Volume > 0
                )
                .Select(price => new
                {
                    price.EquityListingId,
                    price.Date,
                    price.Low,
                    price.High,
                    price.Close,
                })
                .ToListAsync(cancellationToken)
        ).ToLookup(
            price => price.EquityListingId,
            price => new TradedBar(price.Date, price.Low, price.High, price.Close)
        );
        var splitFrom = from.AddDays(-SplitEvents.SameEventWindowDays);
        var splitsByIssuer = (
            await splits
                .GetAll()
                .Where(split =>
                    issuerIds.Contains(split.EquityIssuerId) && split.EffectiveDate >= splitFrom
                )
                .Select(split => new { split.EquityIssuerId, split.EffectiveDate })
                .ToListAsync(cancellationToken)
        ).ToLookup(split => split.EquityIssuerId, split => split.EffectiveDate);
        // Read again, untracked: an enrichment may have rewritten a pair since the sweep's read,
        // and the write below is conditional on the pair read here.
        var current = await listings
            .GetAll()
            .AsNoTracking()
            .Include(listing => listing.Security)
            .Where(listing => listingIds.Contains(listing.Id) && listing.Presentation != null)
            .ToListAsync(cancellationToken);

        var restated = 0;
        foreach (var listing in current)
        {
            if (listing.YahooEnrichmentAttemptedAt is not { } snapshotTime)
                continue;
            var snapshotDate = DateOnly.FromDateTime(snapshotTime);
            var security = listing.Security;
            var bars = barsByListing[listing.Id]
                .Where(bar =>
                    bar.Date
                    >= snapshotDate.AddDays(-MarketCapitalizationBasis.SnapshotLookbackDays)
                )
                .ToList();
            if (bars.Count == 0)
                continue;

            // A split changes the share count, and the count stored here may predate it: the cap
            // is then refreshed from the feed by the split reconciliation, never by arithmetic.
            var newestBar = bars.Max(bar => bar.Date);
            if (
                splitsByIssuer[security.EquityIssuerId]
                    .Any(effective =>
                        effective >= snapshotDate.AddDays(-SplitEvents.SameEventWindowDays)
                        && effective <= newestBar.AddDays(SplitEvents.SameEventWindowDays)
                    )
            )
                continue;

            var target = MarketCapitalizationBasis.OnNewestClose(
                security.SharesOutstanding,
                security.MarketCapitalization,
                listing.QuoteUnitMultiplier,
                snapshotDate,
                bars
            );
            if (target is not { } value || Math.Abs(value - security.MarketCapitalization) < 1)
                continue;
            if (await IsVenueOwned(security, cancellationToken))
                continue;

            if (
                await securities.ReplaceMarketCapitalization(
                    security.Id,
                    security.SharesOutstanding,
                    security.MarketCapitalization,
                    value,
                    cancellationToken
                )
            )
                restated++;
        }
        return restated;
    }

    private async Task<bool> IsVenueOwned(
        CommonStocks.Data.Models.EquitySecurity security,
        CancellationToken cancellationToken
    )
    {
        foreach (var owner in venueOwners)
            if (await owner.Owns(security, cancellationToken))
                return true;
        return false;
    }
}
