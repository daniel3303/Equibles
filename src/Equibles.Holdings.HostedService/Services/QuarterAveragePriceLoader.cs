using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Prices each 13F-held US security at its quarter average close, read from its exact listing's
/// stored daily bars and put on the quarter-end filing basis.
/// </summary>
internal static class QuarterAveragePriceLoader
{
    public static async Task<Dictionary<HolderSecurityKey, decimal>> Load(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        DateOnly previous,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var filed = await LoadFiledPrices(dbContext, reportDate, cancellationToken);
        if (filed.Count == 0)
            return [];
        var filedPrices = filed.ToDictionary(
            row => new HolderSecurityKey(row.EquityIssuerId, row.ListedTicker),
            row => (decimal)row.FiledPricePerShare
        );
        // Listing tickers are matched to stored labels without regard to case.
        var storedKeys = filedPrices
            .Keys.GroupBy(key => (key.IssuerId, Ticker: key.ListedTicker?.ToUpperInvariant()))
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        var issuerIds = filed.Select(row => row.EquityIssuerId).Distinct().ToList();

        var listings = await dbContext
            .Set<EquityListing>()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && issuerIds.Contains(listing.Security.EquityIssuerId)
            )
            .Select(listing => new
            {
                listing.Id,
                listing.Security.EquityIssuerId,
                listing.Ticker,
                listing.IsDirectoryListed,
                IsPresentation = listing.Presentation != null,
            })
            .ToListAsync(cancellationToken);
        var primaryTickers = listings
            .Where(listing => listing.IsPresentation)
            .ToDictionary(listing => listing.EquityIssuerId, listing => listing.Ticker);
        var secondaryTickers = listings
            .Where(listing => listing.IsDirectoryListed && !listing.IsPresentation)
            .ToLookup(listing => listing.EquityIssuerId, listing => listing.Ticker);
        // A sibling ticker two US listings share, or the presentation's own, names no one series.
        var securityByListing = listings
            .Where(listing => !listing.IsPresentation && listing.Ticker != null)
            .GroupBy(listing => (listing.EquityIssuerId, Ticker: listing.Ticker.ToUpperInvariant()))
            .Where(group =>
                group.Count() == 1
                && !string.Equals(
                    group.Key.Ticker,
                    primaryTickers.GetValueOrDefault(group.Key.EquityIssuerId),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(group => (group.Single().Id, group.Key.EquityIssuerId, group.Key.Ticker))
            .Concat(
                listings
                    .Where(listing => listing.IsPresentation)
                    .Select(listing => (listing.Id, listing.EquityIssuerId, Ticker: (string)null))
            )
            .Where(listing => storedKeys.ContainsKey((listing.EquityIssuerId, listing.Ticker)))
            .ToDictionary(
                listing => listing.Id,
                listing => storedKeys[(listing.EquityIssuerId, listing.Ticker)]
            );
        var listingIds = securityByListing.Keys.ToList();

        var sessions = await dbContext
            .Set<EquityDailyStockPrice>()
            .Where(bar =>
                listingIds.Contains(bar.EquityListingId)
                && bar.Date > previous
                && bar.Date <= reportDate
                && bar.Volume > 0
            )
            .GroupBy(bar => bar.EquityListingId)
            .Select(group => new
            {
                ListingId = group.Key,
                Average = group.Average(bar => bar.Close),
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);
        var lastWeekStart = reportDate.AddDays(
            -QuarterAveragePriceCalculator.MaximumLastSessionGapDays
        );
        var lastSessions = (
            await dbContext
                .Set<EquityDailyStockPrice>()
                .Where(bar =>
                    listingIds.Contains(bar.EquityListingId)
                    && bar.Date >= lastWeekStart
                    && bar.Date <= reportDate
                    && bar.Volume > 0
                )
                .Select(bar => new
                {
                    bar.EquityListingId,
                    bar.Date,
                    bar.Close,
                })
                .ToListAsync(cancellationToken)
        ).GroupBy(bar => bar.EquityListingId).ToDictionary(group => group.Key, group => group.MaxBy(bar => bar.Date));

        // The whole effective history, since the basis rules also read splits before the quarter.
        var splits = (
            await StockSplitQueries
                .ForIssuers(dbContext, issuerIds)
                .AsNoTracking()
                .Where(split => split.EffectiveDate <= today)
                .ToListAsync(cancellationToken)
        ).ToLookup(split => split.EquityIssuerId);

        var prices = new Dictionary<HolderSecurityKey, decimal>();
        foreach (var session in sessions)
        {
            if (!lastSessions.TryGetValue(session.ListingId, out var last))
                continue;
            var key = securityByListing[session.ListingId];
            var issuerSplits = splits[key.IssuerId].ToList();
            var factorResolved = SplitBasisResolver.TryResolveFactor(
                reportDate,
                issuerSplits,
                key.ListedTicker,
                primaryTickers.GetValueOrDefault(key.IssuerId),
                secondaryTickers[key.IssuerId].ToList(),
                out var factor
            );
            var average = QuarterAveragePriceCalculator.AveragePrice(
                new QuarterPriceSeries
                {
                    AverageClose = session.Average,
                    LastClose = last.Close,
                    LastSessionDate = last.Date,
                    Sessions = session.Count,
                    ShareCountFactor = factorResolved ? factor : null,
                    // Bars inside a split quarter may sit on either basis, so any issuer split refuses.
                    SplitInsideQuarter = issuerSplits.Any(split =>
                        split.EffectiveDate > previous && split.EffectiveDate <= reportDate
                    ),
                    FiledPricePerShare = filedPrices[key],
                },
                reportDate
            );
            if (average != null)
                prices[key] = average.Value;
        }
        return prices;
    }

    // Each filer's own mark, not the value derived from stored closes, so the check is independent;
    // the median keeps one filer's units error from moving it.
    private static Task<List<QuarterFiledPriceRow>> LoadFiledPrices(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        CancellationToken cancellationToken
    ) =>
        dbContext
            .Database.SqlQuery<QuarterFiledPriceRow>(
                $"""
                SELECT "EquityIssuerId",
                       "ListedTicker",
                       percentile_cont(0.5) WITHIN GROUP (
                           ORDER BY "FiledValue"::float8 / "Shares"
                       ) AS "FiledPricePerShare"
                FROM "InstitutionalHolding"
                WHERE "ReportDate" = {reportDate}
                  AND "FilingType" = {(int)FilingType.Form13F}
                  AND "ShareType" = {(int)ShareType.Shares}
                  AND "OptionType" IS NULL
                  AND "FiledValue" > 0
                  AND "Shares" > 0
                GROUP BY "EquityIssuerId", "ListedTicker"
                """
            )
            .ToListAsync(cancellationToken);
}
