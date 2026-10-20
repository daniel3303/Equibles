using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.BusinessLogic.Links;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// The fund lane: registered funds holding the listing on each series' latest Form NPORT-P, read
/// through the fund directory's <c>LatestNportFilingId</c> and the listing's CUSIP identity. A
/// series nests under the 13F filer its sole adviser reports through (<c>FundSeriesHolderLink</c>);
/// its shares sit inside that filer's 13F report, so funds are never summed.
/// </summary>
public class FundContributor : IHolderSourceContributor
{
    /// <summary>The N-PORT unit code for a balance stated in shares.</summary>
    public const string SharesUnit = "NS";

    // A series whose newest report is older than this stopped filing (liquidated or merged); the
    // form is due 60 days after each fiscal quarter, so 18 months spans staggered calendars.
    private const int CurrentReportDays = 548;

    // How far before a series' latest report its previous public report can sit.
    private const int PriorReportDays = 215;

    private readonly NportFilingRepository _nportRepository;
    private readonly FundSeriesRepository _fundSeriesRepository;
    private readonly IHolderRollupReader _rollupReader;

    public FundContributor(
        NportFilingRepository nportRepository,
        FundSeriesRepository fundSeriesRepository,
        IHolderRollupReader rollupReader
    )
    {
        _nportRepository = nportRepository;
        _fundSeriesRepository = fundSeriesRepository;
        _rollupReader = rollupReader;
    }

    public async Task<IReadOnlyList<StockHolderRow>> Read(
        StockOwnershipScope scope,
        CancellationToken cancellationToken = default
    )
    {
        var stock = scope.Stock;
        if (!await _nportRepository.HasCusipIdentity(stock, scope.ListedTicker, cancellationToken))
            return [];
        var floor = scope.Today.AddDays(-CurrentReportDays);
        var lines = await _nportRepository
            .GetHoldingsByListingCusip(stock, scope.ListedTicker)
            .Where(h => h.Units == SharesUnit && h.PayoffProfile != "Short")
            .Join(
                _fundSeriesRepository
                    .GetAll()
                    .Where(series => series.LatestReportPeriodDate >= floor),
                h => h.NportFilingId,
                series => series.LatestNportFilingId,
                (h, series) =>
                    new
                    {
                        series.IdentityKey,
                        series.SeriesId,
                        series.SeriesName,
                        series.RegistrantName,
                        series.LatestReportPeriodDate,
                        series.LatestFilingDate,
                        h.NportFiling.AccessionNumber,
                        h.Balance,
                        h.ValueUsd,
                        h.PercentValue,
                    }
            )
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (lines.Count == 0)
            return [];

        var splits = scope.SplitsFor(scope.ListedTicker);
        var seriesIds = lines
            .Select(line => line.SeriesId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();
        var latestPeriods = lines
            .Where(line => !string.IsNullOrEmpty(line.SeriesId))
            .GroupBy(line => line.SeriesId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(line => line.LatestReportPeriodDate)
            );
        var priors = await PriorBalances(scope, latestPeriods, cancellationToken);
        var links = await _rollupReader.GetFundSeriesLinks(seriesIds, cancellationToken);

        return lines
            .GroupBy(line => line.IdentityKey)
            .Select(series =>
            {
                var first = series.First();
                var balance = series.Sum(line => line.Balance);
                var shares = RestateBalance(balance, first.LatestReportPeriodDate, splits);
                (decimal Balance, DateOnly ReportPeriodDate)? prior =
                    first.SeriesId != null && priors.TryGetValue(first.SeriesId, out var found)
                        ? found
                        : null;
                long? priorShares = prior is { } p
                    ? RestateBalance(p.Balance, p.ReportPeriodDate, splits)
                    : null;
                return new StockHolderRow
                {
                    HolderKey =
                        first.SeriesId != null
                            ? new StockHolderKey(StockHolderKeyKind.SeriesId, first.SeriesId)
                            : new StockHolderKey(StockHolderKeyKind.Opaque, series.Key),
                    Source = StockHolderSource.Fund,
                    Name = first.SeriesName ?? first.RegistrantName,
                    SeriesId = first.SeriesId,
                    Detail = first.RegistrantName,
                    AdviserHolderId =
                        first.SeriesId != null && links.TryGetValue(first.SeriesId, out var link)
                            ? link.InstitutionalHolderId
                            : null,
                    Shares = shares,
                    FiledShares = balance,
                    Value = (long)Math.Round(series.Sum(line => line.ValueUsd)),
                    ChangeShares = priorShares is { } before ? shares - before : null,
                    PriorAsOf = prior?.ReportPeriodDate,
                    PortfolioWeight = series.Sum(line => line.PercentValue),
                    AsOf = first.LatestReportPeriodDate,
                    FilingDate = first.LatestFilingDate,
                    Accession = first.AccessionNumber,
                    EntityType = HolderEntityType.RegisteredFund,
                    Flags =
                        StockHolderRowFlags.NotSummed
                        | (
                            scope.SplitUnresolved(first.LatestReportPeriodDate, scope.ListedTicker)
                                ? StockHolderRowFlags.SplitUnresolved
                                : StockHolderRowFlags.None
                        ),
                };
            })
            .ToList();
    }

    /// <summary>
    /// A fund's share balance as of its report period, restated onto today's split basis: a split
    /// effective after the period multiplies the count it reported.
    /// </summary>
    public static long RestateBalance(
        decimal balance,
        DateOnly reportPeriodDate,
        IReadOnlyCollection<StockSplit> splits
    ) =>
        (long)
            Math.Round(
                SplitAdjustment.AdjustShareCount(
                    balance,
                    SplitAdjustment.ShareCountFactor(reportPeriodDate, splits)
                ),
                MidpointRounding.AwayFromZero
            );

    // Each series' previous report that held the listing: its latest report period before the
    // series' latest one, the newest filing of that period winning. A series whose previous report
    // did not hold it has none, so its change is unknown rather than a new position.
    private async Task<
        Dictionary<string, (decimal Balance, DateOnly ReportPeriodDate)>
    > PriorBalances(
        StockOwnershipScope scope,
        IReadOnlyDictionary<string, DateOnly> latestPeriods,
        CancellationToken cancellationToken
    )
    {
        if (latestPeriods.Count == 0)
            return [];
        var seriesIds = latestPeriods.Keys.ToList();
        var floor = latestPeriods.Values.Min().AddDays(-PriorReportDays);
        var lines = await _nportRepository
            .GetHoldingsByListingCusip(scope.Stock, scope.ListedTicker)
            .Where(h =>
                h.Units == SharesUnit
                && h.PayoffProfile != "Short"
                && h.NportFiling.SeriesId != null
                && seriesIds.Contains(h.NportFiling.SeriesId)
                && h.NportFiling.ReportPeriodDate >= floor
            )
            .AsNoTracking()
            .Select(h => new
            {
                h.NportFiling.SeriesId,
                h.NportFilingId,
                h.NportFiling.ReportPeriodDate,
                h.NportFiling.FilingDate,
                h.NportFiling.AccessionNumber,
                h.Balance,
            })
            .ToListAsync(cancellationToken);
        return lines
            .Where(line =>
                line.ReportPeriodDate < latestPeriods[line.SeriesId]
                && line.ReportPeriodDate >= latestPeriods[line.SeriesId].AddDays(-PriorReportDays)
            )
            .GroupBy(line => line.SeriesId)
            .ToDictionary(
                series => series.Key,
                series =>
                {
                    var filing = series
                        .GroupBy(line => line.NportFilingId)
                        .OrderByDescending(group => group.First().ReportPeriodDate)
                        .ThenByDescending(group => group.First().FilingDate)
                        .ThenByDescending(
                            group => group.First().AccessionNumber,
                            StringComparer.Ordinal
                        )
                        .First();
                    return (filing.Sum(line => line.Balance), filing.First().ReportPeriodDate);
                }
            );
    }
}
