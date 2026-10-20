using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Holdings.HostedService.Services.Benchmarks;

/// <summary>
/// Weights each published index by an index-tracking fund's N-PORT equity holdings: per index, the
/// configured tracker's complete report with the latest period on or before the quarter end and
/// at most <see cref="WindowDays"/> before it. Holdings resolve to issuers by CUSIP (or the CUSIP
/// inside a US or Canadian ISIN) through the 13F importer's precedence, never by name.
/// </summary>
public class NportSeriesHolderBenchmarkProvider : IHolderBenchmarkProvider
{
    /// <summary>
    /// A report is current for this many days, so a fund reporting on fiscal month ends (a
    /// November, February, May and August year) still has one inside every calendar quarter.
    /// </summary>
    public const int WindowDays = 92;

    /// <summary>Loses a same-distance tie to a source that publishes the index's constituents.</summary>
    public const int Priority = 100;

    private const string EquityCommon = "EC";

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly EquityIssuerRepository _issuerRepository;
    private readonly HolderBenchmarkOptions _options;
    private readonly ILogger<NportSeriesHolderBenchmarkProvider> _logger;

    public NportSeriesHolderBenchmarkProvider(
        EquiblesFinancialDbContext dbContext,
        EquityIssuerRepository issuerRepository,
        IOptions<HolderBenchmarkOptions> options,
        ILogger<NportSeriesHolderBenchmarkProvider> logger
    )
    {
        _dbContext = dbContext;
        _issuerRepository = issuerRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HolderBenchmark>> Load(
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var trackers = _options
            .EffectiveTrackers.Where(tracker =>
                HolderBenchmarkCatalog.Contains(tracker.BenchmarkKey)
                && tracker.BenchmarkKey != HolderBenchmarkCatalog.Aggregate
                && (SeriesIdOf(tracker) != null || CikNormalizer.Canonicalize(tracker.Cik) != null)
            )
            .ToList();
        if (trackers.Count == 0)
            return [];

        var filings = await LoadFilings(trackers, reportDate, cancellationToken);
        var notIndexFunds = await SeriesNotReportedAsIndexFunds(
            filings.Select(filing => filing.SeriesId).Where(id => id != null).Distinct().ToList(),
            reportDate,
            cancellationToken
        );
        var chosen = filings
            .Where(filing =>
                filing.Tracker != null
                && filing.IsComplete
                && (filing.SeriesId == null || !notIndexFunds.Contains(filing.SeriesId))
            )
            .GroupBy(filing => filing.Tracker.BenchmarkKey)
            .Select(group =>
                group
                    .OrderByDescending(filing => filing.ReportPeriodDate)
                    .ThenBy(filing => trackers.IndexOf(filing.Tracker))
                    .ThenByDescending(filing => filing.FilingDate)
                    .ThenByDescending(filing => filing.AccessionNumber, StringComparer.Ordinal)
                    .First()
            )
            .ToList();
        if (chosen.Count == 0)
            return [];

        var benchmarks = await Weigh(chosen, cancellationToken);
        _logger.LogInformation(
            "Holder benchmarks from N-PORT for {ReportDate}: {Benchmarks}",
            reportDate,
            string.Join(
                ", ",
                benchmarks.Select(benchmark =>
                    $"{benchmark.Key} {benchmark.Source} as of {benchmark.AsOf:yyyy-MM-dd}, coverage {benchmark.Coverage:P1}"
                )
            )
        );
        return benchmarks;
    }

    // Every tracker report in the window with its stored and reported holding counts; a report
    // whose stored schedule was narrowed is incomplete and never weighs an index.
    private async Task<List<TrackerFiling>> LoadFilings(
        List<HolderBenchmarkTracker> trackers,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var floor = reportDate.AddDays(-WindowDays);
        var seriesIds = trackers.Select(SeriesIdOf).Where(id => id != null).Distinct().ToList();
        var trustCiks = trackers
            .Select(tracker => CikNormalizer.Canonicalize(tracker.Cik))
            .Where(cik => cik != null)
            .Distinct()
            .ToList();
        // A stored CIK may keep its zero-padding, so both spellings are looked up.
        var cikSpellings = trustCiks
            .SelectMany(cik => new[] { cik, cik.PadLeft(10, '0') })
            .ToList();
        var trustCikByIssuer = (
            await _dbContext
                .Set<EquityIssuer>()
                .AsNoTracking()
                .Where(issuer => cikSpellings.Contains(issuer.Cik))
                .Select(issuer => new { issuer.Id, issuer.Cik })
                .ToListAsync(cancellationToken)
        ).ToDictionary(issuer => issuer.Id, issuer => CikNormalizer.Canonicalize(issuer.Cik));
        var trustIssuerIds = trustCikByIssuer.Keys.Select(id => (Guid?)id).ToList();

        var rows = await _dbContext
            .Set<NportFiling>()
            .AsNoTracking()
            .Where(filing =>
                filing.ReportPeriodDate >= floor
                && filing.ReportPeriodDate <= reportDate
                && (
                    seriesIds.Contains(filing.SeriesId)
                    || (
                        (filing.SeriesId == null || filing.SeriesId == "")
                        && trustIssuerIds.Contains(filing.EquityIssuerId)
                    )
                )
            )
            .Select(filing => new
            {
                filing.Id,
                filing.SeriesId,
                filing.EquityIssuerId,
                filing.ReportPeriodDate,
                filing.FilingDate,
                filing.AccessionNumber,
                filing.ReportedHoldingCount,
                StoredHoldingCount = filing.Holdings.Count(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
            {
                var seriesId = string.IsNullOrWhiteSpace(row.SeriesId)
                    ? null
                    : row.SeriesId.Trim().ToUpperInvariant();
                var trustCik =
                    seriesId == null
                    && row.EquityIssuerId is { } issuerId
                    && trustCikByIssuer.TryGetValue(issuerId, out var cik)
                        ? cik
                        : null;
                return new TrackerFiling
                {
                    Id = row.Id,
                    SeriesId = seriesId,
                    TrustCik = trustCik,
                    ReportPeriodDate = row.ReportPeriodDate,
                    FilingDate = row.FilingDate,
                    AccessionNumber = row.AccessionNumber,
                    IsComplete =
                        row.StoredHoldingCount > 0
                        && row.ReportedHoldingCount == row.StoredHoldingCount,
                    Tracker = trackers.FirstOrDefault(tracker =>
                        seriesId != null
                            ? SeriesIdOf(tracker) == seriesId
                            : trustCik != null
                                && SeriesIdOf(tracker) == null
                                && CikNormalizer.Canonicalize(tracker.Cik) == trustCik
                    ),
                };
            })
            .ToList();
    }

    // A series whose latest N-CEN report up to the quarter end does not mark it an index fund has
    // changed what it is; a series without an N-CEN report yet (or a trust, which files none) stays.
    private async Task<HashSet<string>> SeriesNotReportedAsIndexFunds(
        List<string> seriesIds,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        if (seriesIds.Count == 0)
            return [];
        var reports = await _dbContext
            .Set<NCenFundReport>()
            .AsNoTracking()
            .Where(report =>
                seriesIds.Contains(report.SeriesId)
                && !report.IsSuperseded
                && report.ReportEndingPeriod <= reportDate
            )
            .Select(report => new
            {
                report.SeriesId,
                report.ReportEndingPeriod,
                report.FilingDate,
                report.FundTypes,
            })
            .ToListAsync(cancellationToken);
        var rejected = reports
            .GroupBy(report => report.SeriesId)
            .Select(group =>
                group
                    .OrderByDescending(report => report.ReportEndingPeriod)
                    .ThenByDescending(report => report.FilingDate)
                    .First()
            )
            .Where(report => !report.FundTypes.HasFlag(NCenFundTypes.IndexFund))
            .Select(report => report.SeriesId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var seriesId in rejected)
            _logger.LogWarning(
                "Holder benchmark tracker {SeriesId} is not an index fund on its latest N-CEN up to {ReportDate}; it is skipped",
                seriesId,
                reportDate
            );
        return rejected;
    }

    private async Task<List<HolderBenchmark>> Weigh(
        List<TrackerFiling> chosen,
        CancellationToken cancellationToken
    )
    {
        var filingIds = chosen.Select(filing => filing.Id).ToList();
        var holdings = await _dbContext
            .Set<NportHolding>()
            .AsNoTracking()
            .Where(holding =>
                filingIds.Contains(holding.NportFilingId)
                && holding.AssetCategory == EquityCommon
                && holding.ValueUsd > 0
                && holding.PayoffProfile != "Short"
            )
            .Select(holding => new
            {
                holding.Id,
                holding.NportFilingId,
                holding.Cusip,
                holding.Isin,
                holding.ValueUsd,
            })
            .ToListAsync(cancellationToken);

        var cusips = holdings
            .Select(holding => CusipOf(holding.Cusip, holding.Isin))
            .Where(cusip => cusip != null)
            .Distinct()
            .ToList();
        var claims = await HoldingCusipResolution.Load(
            _issuerRepository,
            _issuerRepository.GetAll(),
            cusips,
            cancellationToken
        );
        var issuersByCusip = HoldingCusipResolution.Resolve(claims, []);

        var holdingsByFiling = holdings.ToLookup(holding => holding.NportFilingId);
        var benchmarks = new List<HolderBenchmark>();
        foreach (var filing in chosen)
        {
            var weights = new Dictionary<Guid, decimal>();
            decimal total = 0,
                resolved = 0;
            foreach (var holding in holdingsByFiling[filing.Id])
            {
                total += holding.ValueUsd;
                // An unresolved name keeps its weight under its own row id, a key no issuer has.
                var key = holding.Id;
                if (
                    CusipOf(holding.Cusip, holding.Isin) is { } cusip
                    && issuersByCusip.TryGetValue(cusip, out var target)
                )
                {
                    key = target.CommonStockId;
                    resolved += holding.ValueUsd;
                }
                weights[key] = weights.GetValueOrDefault(key) + holding.ValueUsd;
            }
            if (total <= 0)
                continue;
            benchmarks.Add(
                new HolderBenchmark
                {
                    Key = filing.Tracker.BenchmarkKey,
                    AsOf = filing.ReportPeriodDate,
                    Weights = weights,
                    Coverage = resolved / total,
                    Priority = Priority,
                    Source =
                        $"N-PORT {(filing.SeriesId ?? $"CIK {filing.TrustCik}")} {filing.AccessionNumber}",
                }
            );
        }
        return benchmarks;
    }

    // A filed CUSIP, or the CUSIP a US or Canadian ISIN carries; a masked all-zero CUSIP is none.
    internal static string CusipOf(string cusip, string isin)
    {
        var filed = cusip?.Trim().ToUpperInvariant();
        if (filed is { Length: 9 } && filed.Any(character => character != '0'))
            return filed;
        var code = isin?.Trim().ToUpperInvariant();
        return code is { Length: 12 } && (code.StartsWith("US") || code.StartsWith("CA"))
            ? code.Substring(2, 9)
            : null;
    }

    private static string SeriesIdOf(HolderBenchmarkTracker tracker) =>
        string.IsNullOrWhiteSpace(tracker.SeriesId)
            ? null
            : tracker.SeriesId.Trim().ToUpperInvariant();

    private sealed class TrackerFiling
    {
        public Guid Id { get; init; }
        public string SeriesId { get; init; }
        public string TrustCik { get; init; }
        public DateOnly ReportPeriodDate { get; init; }
        public DateOnly FilingDate { get; init; }
        public string AccessionNumber { get; init; }
        public bool IsComplete { get; init; }
        public HolderBenchmarkTracker Tracker { get; init; }
    }
}
