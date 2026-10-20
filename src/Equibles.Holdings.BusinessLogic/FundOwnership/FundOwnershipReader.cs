using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.FundOwnership;

/// <summary>
/// Reads the registered-fund ownership the fund-ownership drain publishes, restating each quarter's
/// counts from its quarter end onto today's split basis per listed series.
/// </summary>
[Service]
public class FundOwnershipReader
{
    public const int DefaultPeriods = 4;
    public const int MaxPeriods = 12;

    private readonly FundOwnershipRepository _repository;
    private readonly StockSplitRepository _stockSplitRepository;

    public FundOwnershipReader(
        FundOwnershipRepository repository,
        StockSplitRepository stockSplitRepository
    )
    {
        _repository = repository;
        _stockSplitRepository = stockSplitRepository;
    }

    /// <summary>
    /// The listing's last <paramref name="periods"/> computed quarters, oldest first. An ETF series
    /// or sibling class reads its exact listing; a primary operating company reads every class, as
    /// its 13F holder mix does.
    /// </summary>
    public async Task<FundOwnershipHistory> ReadRecent(
        EquityIssuer stock,
        string requestedTicker,
        int periods = DefaultPeriods,
        CancellationToken cancellationToken = default
    )
    {
        var listedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, requestedTicker);
        var exactListing = SecondaryTickerPolicy.RequiresExactListingScope(stock, listedTicker);
        var primaryTicker = stock.Presentation.Listing.Ticker;
        var states = await _repository
            .GetQuarterStates()
            .AsNoTracking()
            .Where(state => state.ComputedAt != null)
            .OrderByDescending(state => state.ReportDate)
            .Take(Math.Clamp(periods, 1, MaxPeriods))
            .Select(state => new { state.ReportDate, state.ComputedAt })
            .ToListAsync(cancellationToken);
        if (states.Count == 0)
            return new FundOwnershipHistory
            {
                ListedTicker = listedTicker,
                ExactListing = exactListing,
                Unavailable =
                    "Registered-fund ownership is not computed yet; it is built from Form NPORT-P reports once each quarter's filing window closes.",
            };

        var dates = states.Select(state => state.ReportDate).ToList();
        var listingKey = string.Equals(
            listedTicker,
            primaryTicker,
            StringComparison.OrdinalIgnoreCase
        )
            ? null
            : listedTicker;
        var query = _repository.GetByIssuer(stock.Id, dates).AsNoTracking();
        if (exactListing)
            query =
                listingKey == null
                    ? query.Where(row => row.ListedTicker == null)
                    : query.Where(row => row.ListedTicker == listingKey);
        var rows = await query.ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var splits = await _stockSplitRepository
            .GetEffectiveByStock(stock.Id, today)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        // Shares outstanding counts the whole company, so a sibling class or another fund of the
        // trust read on its own shows share counts only, as the holder list does.
        var otherListing = exactListing && listingKey != null;
        var sharesOutstanding = otherListing
            ? null
            : stock.Presentation.Listing.Security?.SharesOutstanding;
        var outstanding = sharesOutstanding > 0 ? sharesOutstanding : null;

        var quarters = states
            .OrderBy(state => state.ReportDate)
            .Select(state =>
            {
                var quarterRows = rows.Where(row => row.ReportDate == state.ReportDate).ToList();
                var unresolved = quarterRows
                    .Select(row => row.ListedTicker)
                    .Distinct()
                    .Any(ticker =>
                        ListingId(stock, ticker) is { } listingId
                        && ShareCountSplitScope.HasUnresolvedBasis(
                            splits,
                            listingId,
                            state.ReportDate
                        )
                    );
                return BuildQuarter(
                    state.ReportDate,
                    state.ComputedAt,
                    quarterRows,
                    row =>
                        SplitAdjustment.AdjustShareCount(
                            row.Shares,
                            row.ReportDate,
                            ShareCountSplitScope.ForListedSeries(
                                splits,
                                primaryTicker,
                                row.ListedTicker ?? primaryTicker
                            )
                        ),
                    unresolved ? null : outstanding,
                    unresolved
                );
            })
            .ToList();

        return new FundOwnershipHistory
        {
            ListedTicker = listedTicker,
            ExactListing = exactListing,
            Quarters = quarters,
            SharesOutstanding = outstanding,
            PercentUnavailable =
                otherListing ? "shares outstanding is an issuer-wide count, not one listing's"
                : outstanding == null ? "no shares outstanding count is on record"
                : null,
        };
    }

    /// <summary>
    /// Every issuer's registered-fund shares in the newest computed quarter, restated onto today's
    /// split basis: the index funds' and index ETFs' shares and every fund's. An operating company
    /// counts every class of its common stock, as its 13F holder mix does, so the figures divide by
    /// its issuer-wide shares outstanding; an exchange-traded product counts its own listing only,
    /// since a trust's other listings are other funds. An issuer whose split basis after the
    /// quarter cannot be attributed is listed as unresolved.
    /// </summary>
    public Task<FundIndexOwnershipSnapshot> ReadLatestByIssuer(
        CancellationToken cancellationToken = default
    ) => ReadByIssuer(null, null, cancellationToken);

    /// <summary>
    /// The registered-fund shares of <paramref name="issuerIds"/> (every issuer when null) in the
    /// newest computed quarter on or before <paramref name="onOrBefore"/> (the newest when null),
    /// restated as <see cref="ReadLatestByIssuer"/> restates them. An issuer the quarter's funds did
    /// not hold is absent, which reads as none held.
    /// </summary>
    public async Task<FundIndexOwnershipSnapshot> ReadByIssuer(
        DateOnly? onOrBefore,
        IReadOnlyCollection<Guid> issuerIds,
        CancellationToken cancellationToken = default
    )
    {
        var quarters = _repository.GetComputedQuarters();
        if (onOrBefore is { } ceiling)
            quarters = quarters.Where(date => date <= ceiling);
        var latest = await quarters
            .Select(date => (DateOnly?)date)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not { } reportDate)
            return new FundIndexOwnershipSnapshot(
                null,
                new Dictionary<Guid, IssuerFundShares>(),
                new HashSet<Guid>()
            );

        var quarterRows = _repository.GetByQuarter(reportDate).AsNoTracking();
        if (issuerIds != null)
            quarterRows = quarterRows.Where(row => issuerIds.Contains(row.EquityIssuerId));
        var rows = await quarterRows
            .GroupBy(row => new { row.EquityIssuerId, row.ListedTicker })
            .Select(group => new
            {
                group.Key.EquityIssuerId,
                group.Key.ListedTicker,
                IndexShares = group
                    .Where(row =>
                        row.Bucket == FundOwnershipBucket.IndexFund
                        || row.Bucket == FundOwnershipBucket.IndexEtf
                    )
                    .Sum(row => row.Shares),
                FundShares = group.Sum(row => row.Shares),
            })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var splits = (
            await _repository
                .GetSplitsBetween(
                    rows.Select(row => row.EquityIssuerId).Distinct().ToList(),
                    reportDate,
                    today
                )
                .AsNoTracking()
                .ToListAsync(cancellationToken)
        )
            .GroupBy(split => split.EquityIssuerId)
            .ToDictionary(group => group.Key, group => group.ToList());
        // Listings place a sibling row's split history and tell a fund trust from a company.
        var listingIssuers = splits
            .Keys.Concat(
                rows.Where(row => row.ListedTicker != null).Select(row => row.EquityIssuerId)
            )
            .Distinct()
            .ToList();
        var listings =
            listingIssuers.Count == 0
                ? []
                : await _repository
                    .GetUsListings(listingIssuers)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
        var listingsByIssuer = listings
            .GroupBy(listing => listing.EquityIssuerId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var byIssuer = new Dictionary<Guid, IssuerFundShares>();
        var excluded = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (excluded.Contains(row.EquityIssuerId))
                continue;
            var issuerListings = listingsByIssuer.GetValueOrDefault(row.EquityIssuerId) ?? [];
            if (
                row.ListedTicker != null
                && issuerListings.Any(listing =>
                    listing.IsPresentation && listing.IsReferenceListed
                )
            )
                continue;
            long indexShares = row.IndexShares,
                fundShares = row.FundShares;
            if (splits.TryGetValue(row.EquityIssuerId, out var issuerSplits))
            {
                var primary = issuerListings.FirstOrDefault(listing => listing.IsPresentation);
                var listing =
                    row.ListedTicker == null
                        ? primary
                        : issuerListings.FirstOrDefault(candidate =>
                            string.Equals(
                                candidate.Ticker,
                                row.ListedTicker,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );
                if (
                    primary == null
                    || listing == null
                    || ShareCountSplitScope.HasUnresolvedBasis(
                        issuerSplits,
                        listing.ListingId,
                        reportDate
                    )
                )
                {
                    excluded.Add(row.EquityIssuerId);
                    byIssuer.Remove(row.EquityIssuerId);
                    continue;
                }
                var factor = SplitAdjustment.ShareCountFactor(
                    reportDate,
                    ShareCountSplitScope.ForListedSeries(
                        issuerSplits,
                        primary.Ticker,
                        listing.Ticker
                    )
                );
                indexShares = SplitAdjustment.AdjustShareCount(indexShares, factor);
                fundShares = SplitAdjustment.AdjustShareCount(fundShares, factor);
            }
            var current = byIssuer.GetValueOrDefault(row.EquityIssuerId);
            byIssuer[row.EquityIssuerId] = new IssuerFundShares(
                (current?.IndexShares ?? 0) + indexShares,
                (current?.FundShares ?? 0) + fundShares
            );
        }
        return new FundIndexOwnershipSnapshot(reportDate, byIssuer, excluded);
    }

    /// <summary>
    /// The registered-fund part of each given 13F filer's position in the listing at a quarter, a
    /// floor restated onto today's split basis; filers without a row are absent.
    /// </summary>
    public async Task<Dictionary<Guid, AdvisedFundShares>> ReadAdvisedFundShares(
        EquityIssuer stock,
        string listedTicker,
        bool exactListing,
        DateOnly reportDate,
        IReadOnlyCollection<Guid> holderIds,
        CancellationToken cancellationToken = default
    )
    {
        if (holderIds.Count == 0)
            return [];
        var primaryTicker = stock.Presentation.Listing.Ticker;
        var listingKey = string.Equals(
            listedTicker,
            primaryTicker,
            StringComparison.OrdinalIgnoreCase
        )
            ? null
            : listedTicker;
        var query = _repository
            .GetAdvisedFundShares(stock.Id, reportDate, holderIds)
            .AsNoTracking();
        if (exactListing)
            query =
                listingKey == null
                    ? query.Where(row => row.ListedTicker == null)
                    : query.Where(row => row.ListedTicker == listingKey);
        var rows = await query.ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];
        var splits = await _stockSplitRepository
            .GetEffectiveByStock(stock.Id, DateOnly.FromDateTime(DateTime.UtcNow))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        long Restate(long shares, string ticker) =>
            SplitAdjustment.AdjustShareCount(
                shares,
                reportDate,
                ShareCountSplitScope.ForListedSeries(splits, primaryTicker, ticker ?? primaryTicker)
            );
        return rows.GroupBy(row => row.InstitutionalHolderId)
            .ToDictionary(
                group => group.Key,
                group => new AdvisedFundShares(
                    group.Key,
                    reportDate,
                    group.Sum(row => Restate(row.HolderShares, row.ListedTicker)),
                    group.Sum(row => Restate(row.FundShares, row.ListedTicker)),
                    group.Sum(row => Restate(row.IndexFundShares, row.ListedTicker)),
                    group.Sum(row => row.SeriesCount),
                    group.Sum(row => row.IndexSeriesCount),
                    group.Any(row => row.IsCapped)
                )
            );
    }

    /// <summary>Builds one quarter with every bucket present, in bucket order.</summary>
    public static FundOwnershipQuarter BuildQuarter(
        DateOnly reportDate,
        DateTime? computedAt,
        IReadOnlyList<StockQuarterlyFundOwnership> rows,
        Func<StockQuarterlyFundOwnership, long> restate,
        long? sharesOutstanding,
        bool splitUnresolved
    )
    {
        decimal? Percent(long shares) =>
            sharesOutstanding is > 0 and var outstanding
                ? Math.Round(shares * 100m / outstanding, 4)
                : null;
        var buckets = FundOwnershipBuckets
            .Ordered.Select(bucket =>
            {
                var bucketRows = rows.Where(row => row.Bucket == bucket.Value).ToList();
                var shares = bucketRows.Sum(restate);
                return new FundOwnershipBucketShare
                {
                    Bucket = bucket.Value,
                    Token = bucket.Token,
                    Label = bucket.Label,
                    Shares = shares,
                    Value = bucketRows.Sum(row => row.Value),
                    SeriesCount = bucketRows.Sum(row => row.SeriesCount),
                    OldestReportPeriod =
                        bucketRows.Count == 0
                            ? null
                            : bucketRows.Min(row => row.OldestReportPeriod),
                    NewestReportPeriod =
                        bucketRows.Count == 0
                            ? null
                            : bucketRows.Max(row => row.NewestReportPeriod),
                    PercentOfOutstanding = Percent(shares),
                    SplitUnresolvedShares = bucketRows.Sum(row => row.SplitUnresolvedShares),
                };
            })
            .ToList();
        var totalShares = buckets.Sum(bucket => bucket.Shares);
        var withRows = buckets.Where(bucket => bucket.SeriesCount > 0).ToList();
        return new FundOwnershipQuarter
        {
            ReportDate = reportDate,
            ComputedAt = computedAt,
            Buckets = buckets,
            SplitUnresolved = splitUnresolved,
            Total = new FundOwnershipBucketShare
            {
                Token = "all",
                Label = "All registered funds",
                Shares = totalShares,
                Value = buckets.Sum(bucket => bucket.Value),
                SeriesCount = buckets.Sum(bucket => bucket.SeriesCount),
                OldestReportPeriod =
                    withRows.Count == 0 ? null : withRows.Min(bucket => bucket.OldestReportPeriod),
                NewestReportPeriod =
                    withRows.Count == 0 ? null : withRows.Max(bucket => bucket.NewestReportPeriod),
                PercentOfOutstanding = Percent(totalShares),
                SplitUnresolvedShares = buckets.Sum(bucket => bucket.SplitUnresolvedShares),
            },
        };
    }

    private static Guid? ListingId(EquityIssuer stock, string listedTicker)
    {
        if (listedTicker == null)
            return stock.Presentation.EquityListingId;
        return stock
            .Securities.SelectMany(security => security.Listings)
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && string.Equals(listing.Ticker, listedTicker, StringComparison.OrdinalIgnoreCase)
            )
            .Select(listing => (Guid?)listing.Id)
            .FirstOrDefault();
    }
}

/// <summary>An issuer's registered-fund shares on today's split basis.</summary>
public sealed record IssuerFundShares(long IndexShares, long FundShares);

/// <summary>
/// Issuers' registered-fund shares in one computed quarter; <paramref name="Unresolved"/> lists the
/// issuers whose split basis after it cannot be attributed, so they have no figure.
/// </summary>
public sealed record FundIndexOwnershipSnapshot(
    DateOnly? ReportDate,
    IReadOnlyDictionary<Guid, IssuerFundShares> ByIssuer,
    IReadOnlySet<Guid> Unresolved
)
{
    /// <summary>
    /// The index funds' and index ETFs' share of <paramref name="sharesOutstanding"/> as a fraction:
    /// zero when the quarter's funds held none, null without a quarter, a share count or a basis.
    /// </summary>
    public decimal? IndexFraction(Guid issuerId, long? sharesOutstanding) =>
        ReportDate == null || sharesOutstanding is not > 0 || Unresolved.Contains(issuerId)
            ? null
            : (decimal)(ByIssuer.GetValueOrDefault(issuerId)?.IndexShares ?? 0)
                / sharesOutstanding.Value;
}
