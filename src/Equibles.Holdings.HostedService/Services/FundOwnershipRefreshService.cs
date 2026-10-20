using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.FundOwnership;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services.Benchmarks;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Rebuilds one closed quarter's <see cref="StockQuarterlyFundOwnership"/> and
/// <see cref="HolderStockAdvisedFundShares"/> rows from registered funds' Form NPORT-P reports and
/// publishes them, with the quarter's <see cref="FundOwnershipQuarterState"/>, in one transaction.
/// Each series counts its latest report whose portfolio date falls inside the calendar quarter,
/// classified by its latest Form N-CEN fund report on or before the quarter end, never by name.
/// The holding lines are summed in the database, so memory holds one row per bucket, linked filer,
/// security and portfolio date rather than the quarter's million-odd lines.
/// </summary>
[Service]
public class FundOwnershipRefreshService
{
    /// <summary>Bump when the rows change meaning, so the drain rebuilds every quarter. 1: first.</summary>
    public const int CalculationVersion = 1;

    /// <summary>
    /// Days after a quarter end before it is computed: NPORT-P is due 60 days after a fiscal quarter
    /// end, and the last fiscal month inside a calendar quarter can end on the quarter end itself.
    /// Later reports and amendments change the fingerprint and rebuild the quarter.
    /// </summary>
    public const int FilingWindowDays = 75;

    /// <summary>
    /// A fund's latest N-CEN must end within this many months before the quarter end: the form is
    /// yearly and reaches SEC's data sets months after its period ends.
    /// </summary>
    public const int NCenLookbackMonths = 21;

    /// <summary>
    /// A published quarter older than this is rebuilt even with unchanged inputs, which picks up
    /// CUSIP identities and split rows captured since.
    /// </summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);

    internal const string SharesUnit = "NS";

    internal const int PublishBatchSize = 2_000;

    // The first key of the transaction advisory lock that serializes one quarter's publishes.
    private const int PublishLockKey = 0x46_4F_57_4E;

    // A quarter-wide aggregate over a million holding lines outlasts the default 30-second timeout.
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly EquityIssuerRepository _issuerRepository;
    private readonly HoldingsCorpusCoverage _corpusCoverage;
    private readonly ILogger<FundOwnershipRefreshService> _logger;

    public FundOwnershipRefreshService(
        EquiblesFinancialDbContext dbContext,
        EquityIssuerRepository issuerRepository,
        HoldingsCorpusCoverage corpusCoverage,
        ILogger<FundOwnershipRefreshService> logger
    )
    {
        _dbContext = dbContext;
        _issuerRepository = issuerRepository;
        _corpusCoverage = corpusCoverage;
        _logger = logger;
    }

    /// <summary>The first quarter computed: the 13F corpus's, so advised shares always have a 13F side.</summary>
    public DateOnly FirstQuarter => _corpusCoverage.CoverageStartDate;

    /// <summary>A covered calendar quarter end whose NPORT-P filing window has passed.</summary>
    public bool IsComputable(DateOnly reportDate, DateOnly today) =>
        reportDate >= FirstQuarter
        && HoldingsCorpusCoverage.IsCalendarQuarterEnd(reportDate)
        && reportDate.AddDays(FilingWindowDays) <= today;

    /// <summary>Every computable quarter, newest first.</summary>
    public List<DateOnly> ComputableQuarters(DateOnly today)
    {
        var quarters = new List<DateOnly>();
        var quarter = HoldingsCorpusCoverage.FirstQuarterEndOnOrAfter(FirstQuarter);
        for (; IsComputable(quarter, today); quarter = quarter.AddDays(1).AddMonths(3).AddDays(-1))
            quarters.Add(quarter);
        quarters.Reverse();
        return quarters;
    }

    /// <summary>
    /// Each quarter's input fingerprint: its N-PORT filings (count, newest row, parser versions),
    /// the N-CEN fund reports up to it, the fund-to-filer links and its 13F intelligence rebuild.
    /// Read with one grouped query per source, so a tick never scans the filings once per quarter.
    /// </summary>
    public async Task<Dictionary<DateOnly, string>> Fingerprints(
        IReadOnlyCollection<DateOnly> quarters,
        CancellationToken cancellationToken
    )
    {
        if (quarters.Count == 0)
            return [];
        _dbContext.Database.SetCommandTimeout(CommandTimeout);
        var floor = HoldingsCorpusCoverage.PreviousQuarterEnd(quarters.Min());
        var nport = (
            await _dbContext
                .Database.SqlQuery<FundOwnershipNportInputRow>(
                    $"""
                    SELECT (date_trunc('quarter', "ReportPeriodDate") + interval '3 months' - interval '1 day')::date AS "Quarter",
                           count(*)::int AS "Filings",
                           max("CreationTime") AS "Latest",
                           sum("ParserVersion")::bigint AS "ParserVersions"
                    FROM "NportFiling"
                    WHERE "ReportPeriodDate" > {floor}
                    GROUP BY 1
                    """
                )
                .ToListAsync(cancellationToken)
        ).ToDictionary(row => row.Quarter);
        var ncen = await _dbContext
            .Database.SqlQuery<FundOwnershipNCenInputRow>(
                $"""
                SELECT (date_trunc('quarter', "ReportEndingPeriod") + interval '3 months' - interval '1 day')::date AS "Quarter",
                       count(*) FILTER (WHERE NOT "IsSuperseded")::int AS "Reports",
                       max("FilingDate") AS "LatestFiling"
                FROM "NCenFundReport"
                GROUP BY 1
                """
            )
            .ToListAsync(cancellationToken);
        var links = await _dbContext
            .Database.SqlQuery<string>(
                $"""
                SELECT coalesce(md5(string_agg("SeriesId" || ':' || "InstitutionalHolderId"::text, ',' ORDER BY "SeriesId")), '') AS "Value"
                FROM "FundSeriesHolderLink"
                """
            )
            .SingleAsync(cancellationToken);
        var intelligence = await _dbContext
            .Set<HolderIntelligenceQuarterState>()
            .AsNoTracking()
            .Where(state => quarters.Contains(state.ReportDate))
            .ToDictionaryAsync(
                state => state.ReportDate,
                state => state.ComputedAt,
                cancellationToken
            );

        return quarters.ToDictionary(
            quarter => quarter,
            quarter =>
            {
                var filings = nport.GetValueOrDefault(quarter);
                var reports = ncen.Where(row => row.Quarter <= quarter).ToList();
                var latestReport =
                    reports.Count == 0 ? (DateOnly?)null : reports.Max(row => row.LatestFiling);
                return string.Join(
                    '|',
                    $"v{CalculationVersion}",
                    $"np:{filings?.Filings ?? 0}:{filings?.Latest.Ticks ?? 0}:{filings?.ParserVersions ?? 0}",
                    $"nc:{reports.Sum(row => row.Reports)}:{latestReport:yyyyMMdd}",
                    $"ln:{links}",
                    $"13f:{intelligence.GetValueOrDefault(quarter)?.Ticks ?? 0}"
                );
            }
        );
    }

    /// <summary>
    /// Replaces the quarter's rows and its state in one transaction. Returns false, writing nothing,
    /// for a quarter that is not computable.
    /// </summary>
    public async Task<bool> RebuildQuarter(
        DateOnly reportDate,
        string fingerprint,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        if (!IsComputable(reportDate, today))
            return false;
        _dbContext.Database.SetCommandTimeout(CommandTimeout);

        var filings = await LatestFilings(reportDate, cancellationToken);
        var buckets = await Classify(filings, reportDate, cancellationToken);
        var links = await _dbContext
            .Set<FundSeriesHolderLink>()
            .AsNoTracking()
            .Select(link => new { link.SeriesId, link.InstitutionalHolderId })
            .ToDictionaryAsync(
                link => link.SeriesId.Trim().ToUpperInvariant(),
                link => link.InstitutionalHolderId,
                cancellationToken
            );
        var lines = await LoadLines(filings, buckets, links, cancellationToken);

        var targets = await ResolveCusips(lines, cancellationToken);
        var issuerIds = targets.Values.Select(target => target.CommonStockId).Distinct().ToList();
        var restater = await LoadRestater(issuerIds, reportDate, cancellationToken);

        var stockRows =
            new Dictionary<(Guid, string, FundOwnershipBucket), StockQuarterlyFundOwnership>();
        var advised = new Dictionary<(Guid, Guid, string), HolderStockAdvisedFundShares>();
        foreach (var line in lines)
        {
            if (!targets.TryGetValue(line.Cusip, out var target))
                continue;
            var bucket = (FundOwnershipBucket)line.Bucket;
            var (shares, unresolved) = restater.Restate(
                target.CommonStockId,
                target.ListedTicker,
                line.Balance,
                line.ReportPeriodDate
            );
            var key = (target.CommonStockId, target.ListedTicker, bucket);
            if (!stockRows.TryGetValue(key, out var row))
            {
                row = new StockQuarterlyFundOwnership
                {
                    EquityIssuerId = target.CommonStockId,
                    ListedTicker = target.ListedTicker,
                    ReportDate = reportDate,
                    Bucket = bucket,
                    OldestReportPeriod = line.ReportPeriodDate,
                    NewestReportPeriod = line.ReportPeriodDate,
                };
                stockRows[key] = row;
            }
            row.Shares += shares;
            if (unresolved)
                row.SplitUnresolvedShares += shares;
            row.Value += line.Value;
            row.SeriesCount += line.SeriesCount;
            if (line.ReportPeriodDate < row.OldestReportPeriod)
                row.OldestReportPeriod = line.ReportPeriodDate;
            if (line.ReportPeriodDate > row.NewestReportPeriod)
                row.NewestReportPeriod = line.ReportPeriodDate;

            if (line.HolderId is not { } holderId)
                continue;
            var advisedKey = (holderId, target.CommonStockId, target.ListedTicker);
            if (!advised.TryGetValue(advisedKey, out var fund))
            {
                fund = new HolderStockAdvisedFundShares
                {
                    InstitutionalHolderId = holderId,
                    EquityIssuerId = target.CommonStockId,
                    ListedTicker = target.ListedTicker,
                    ReportDate = reportDate,
                };
                advised[advisedKey] = fund;
            }
            fund.FundShares += shares;
            fund.SeriesCount += line.SeriesCount;
            if (FundOwnershipBuckets.IsIndex(bucket))
            {
                fund.IndexFundShares += shares;
                fund.IndexSeriesCount += line.SeriesCount;
            }
        }

        var advisedRows = await CapAtThirteenF(
            advised.Values.ToList(),
            reportDate,
            cancellationToken
        );
        var unclassified = buckets.Count(entry => entry.Value == FundOwnershipBucket.Unclassified);

        await BeforePublish(reportDate);
        await Publish(
            reportDate,
            stockRows.Values.ToList(),
            advisedRows,
            fingerprint,
            filings.Count,
            unclassified,
            cancellationToken
        );

        _logger.LogInformation(
            "Fund ownership for {ReportDate}: {Series} series ({Unclassified} unclassified), {Lines} summed lines, {Rows} listing rows, {Advised} advised-share rows; value by bucket: {Buckets}",
            reportDate,
            filings.Count,
            unclassified,
            lines.Count,
            stockRows.Count,
            advisedRows.Count,
            string.Join(
                ", ",
                stockRows
                    .Values.GroupBy(row => row.Bucket)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key} {group.Sum(row => row.Value) / 1e9m:0.0}B")
            )
        );
        return true;
    }

    // Each series' latest NPORT-P in the calendar quarter: the newest portfolio date, then filing
    // date, then accession. A non-empty series id is authoritative across the issuer-feed and sweep
    // populations; an id-less report is its registrant's single fund.
    private Task<List<FundOwnershipFilingRow>> LatestFilings(
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var previous = HoldingsCorpusCoverage.PreviousQuarterEnd(reportDate);
        return _dbContext
            .Database.SqlQuery<FundOwnershipFilingRow>(
                $"""
                SELECT DISTINCT ON (f."Key")
                       f."Id", f."SeriesId", f."EquityIssuerId", f."RegistrantCik", f."ReportPeriodDate"
                FROM (
                    SELECT n."Id",
                           NULLIF(upper(btrim(n."SeriesId")), '') AS "SeriesId",
                           n."EquityIssuerId",
                           n."RegistrantCik",
                           n."ReportPeriodDate",
                           n."FilingDate",
                           n."AccessionNumber",
                           COALESCE(
                               NULLIF(upper(btrim(n."SeriesId")), ''),
                               CASE WHEN n."EquityIssuerId" IS NOT NULL
                                    THEN 'cs:' || n."EquityIssuerId"::text
                                    ELSE 'rc:' || ltrim(n."RegistrantCik", '0') END
                           ) AS "Key"
                    FROM "NportFiling" n
                    WHERE n."ReportPeriodDate" > {previous}
                      AND n."ReportPeriodDate" <= {reportDate}
                ) f
                ORDER BY f."Key", f."ReportPeriodDate" DESC, f."FilingDate" DESC, f."AccessionNumber" DESC
                """
            )
            .ToListAsync(cancellationToken);
    }

    // The bucket of every chosen report from the series' latest current N-CEN fund report; an
    // id-less report takes the one fund its registrant's latest N-CEN reports, matched by CIK.
    private async Task<Dictionary<Guid, FundOwnershipBucket>> Classify(
        List<FundOwnershipFilingRow> filings,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var since = reportDate.AddMonths(-NCenLookbackMonths);
        var current = _dbContext
            .Set<NCenFundReport>()
            .AsNoTracking()
            .Where(report =>
                !report.IsSuperseded
                && report.ReportEndingPeriod >= since
                && report.ReportEndingPeriod <= reportDate
            );

        var seriesIds = filings
            .Where(filing => filing.SeriesId != null)
            .Select(filing => filing.SeriesId)
            .Distinct()
            .ToList();
        var seriesReports = await current
            .Where(report => report.SeriesId != null && seriesIds.Contains(report.SeriesId))
            .Select(report => new NCenClassification(
                report.SeriesId,
                report.ReportEndingPeriod,
                report.FilingDate,
                report.FundTypes,
                report.ExchangeListings.Any(),
                1
            ))
            .ToListAsync(cancellationToken);
        var bySeries = Latest(seriesReports, report => report.Key.Trim().ToUpperInvariant());

        var idless = filings.Where(filing => filing.SeriesId == null).ToList();
        var idlessIssuers = idless
            .Where(filing => filing.EquityIssuerId != null)
            .Select(filing => filing.EquityIssuerId.Value)
            .Distinct()
            .ToList();
        var issuerCiks = await _dbContext
            .Set<EquityIssuer>()
            .AsNoTracking()
            .Where(issuer => idlessIssuers.Contains(issuer.Id))
            .Select(issuer => new { issuer.Id, issuer.Cik })
            .ToDictionaryAsync(
                issuer => issuer.Id,
                issuer => CikNormalizer.Canonicalize(issuer.Cik),
                cancellationToken
            );
        string CikOf(FundOwnershipFilingRow filing) =>
            filing.EquityIssuerId is { } issuerId
                ? issuerCiks.GetValueOrDefault(issuerId)
                : CikNormalizer.Canonicalize(filing.RegistrantCik);
        var ciks = idless.Select(CikOf).Where(cik => cik != null).Distinct().ToList();
        var cikSpellings = ciks.SelectMany(cik => new[] { cik, cik.PadLeft(10, '0') }).ToList();
        var registrantReports = await current
            .Where(report =>
                (report.SeriesId == null || report.SeriesId == "")
                && cikSpellings.Contains(report.RegistrantCik)
            )
            .Select(report => new NCenClassification(
                report.RegistrantCik,
                report.ReportEndingPeriod,
                report.FilingDate,
                report.FundTypes,
                report.ExchangeListings.Any(),
                report.RegistrantReport.Funds.Count(fund => !fund.IsSuperseded)
            ))
            .ToListAsync(cancellationToken);
        // A registrant whose latest report names several funds cannot say which one filed the
        // id-less N-PORT, so that report classifies none.
        var byRegistrant = Latest(
            registrantReports,
            report => CikNormalizer.Canonicalize(report.Key)
        );

        return filings.ToDictionary(
            filing => filing.Id,
            filing =>
            {
                var report =
                    filing.SeriesId != null ? bySeries.GetValueOrDefault(filing.SeriesId)
                    : CikOf(filing) is { } cik ? byRegistrant.GetValueOrDefault(cik)
                    : null;
                return report is { FundCount: 1 }
                    ? FundOwnershipBuckets.Classify(report.FundTypes, report.HasExchangeListing)
                    : FundOwnershipBucket.Unclassified;
            }
        );
    }

    private static Dictionary<string, NCenClassification> Latest(
        IEnumerable<NCenClassification> reports,
        Func<NCenClassification, string> key
    ) =>
        reports
            .Where(report => key(report) != null)
            .GroupBy(key)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .OrderByDescending(report => report.ReportEndingPeriod)
                        .ThenByDescending(report => report.FilingDate)
                        .First()
            );

    // Long share positions of the chosen reports, summed in the database per bucket, linked filer,
    // CUSIP (or the CUSIP a US or Canadian ISIN carries) and portfolio date.
    private async Task<List<FundOwnershipLineRow>> LoadLines(
        List<FundOwnershipFilingRow> filings,
        Dictionary<Guid, FundOwnershipBucket> buckets,
        Dictionary<string, Guid> links,
        CancellationToken cancellationToken
    )
    {
        if (filings.Count == 0)
            return [];
        var ids = filings.Select(filing => filing.Id).ToArray();
        var bucketValues = filings.Select(filing => (int)buckets[filing.Id]).ToArray();
        var holders = filings
            .Select(filing =>
                filing.SeriesId != null && links.TryGetValue(filing.SeriesId, out var holderId)
                    ? holderId
                    : (Guid?)null
            )
            .ToArray();
        var periods = filings.Select(filing => filing.ReportPeriodDate).ToArray();
        return await _dbContext
            .Database.SqlQuery<FundOwnershipLineRow>(
                $"""
                SELECT l."Bucket", l."HolderId", l."Cusip", l."ReportPeriodDate",
                       sum(l."Balance") AS "Balance",
                       sum(l."Value") AS "Value",
                       count(DISTINCT l."FilingId")::int AS "SeriesCount"
                FROM (
                    SELECT c.bucket AS "Bucket",
                           c.holder AS "HolderId",
                           c.period AS "ReportPeriodDate",
                           c.id AS "FilingId",
                           h."Balance",
                           h."ValueUsd" AS "Value",
                           CASE
                               WHEN length(btrim(h."Cusip")) = 9 AND btrim(h."Cusip") !~ '^0+$'
                                   THEN upper(btrim(h."Cusip"))
                               WHEN length(btrim(h."Isin")) = 12 AND upper(btrim(h."Isin")) ~ '^(US|CA)'
                                   THEN substr(upper(btrim(h."Isin")), 3, 9)
                           END AS "Cusip"
                    FROM unnest({ids}, {bucketValues}, {holders}, {periods}) AS c(id, bucket, holder, period)
                    JOIN "NportHolding" h ON h."NportFilingId" = c.id
                    WHERE h."Units" = {SharesUnit}
                      AND h."Balance" > 0
                      AND coalesce(h."PayoffProfile", '') <> 'Short'
                ) l
                WHERE l."Cusip" IS NOT NULL
                GROUP BY l."Bucket", l."HolderId", l."Cusip", l."ReportPeriodDate"
                """
            )
            .ToListAsync(cancellationToken);
    }

    // The 13F importer's CUSIP precedence over every issuer, retired ones included, so a quarter
    // before a delisting still resolves.
    private async Task<Dictionary<string, CusipTarget>> ResolveCusips(
        List<FundOwnershipLineRow> lines,
        CancellationToken cancellationToken
    )
    {
        var cusips = lines.Select(line => line.Cusip).Distinct().ToList();
        if (cusips.Count == 0)
            return [];
        var claims = await HoldingCusipResolution.Load(
            _issuerRepository,
            _issuerRepository.GetAll(),
            cusips,
            cancellationToken
        );
        return HoldingCusipResolution.Resolve(claims, []);
    }

    private async Task<FundShareRestater> LoadRestater(
        List<Guid> issuerIds,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var splits =
            issuerIds.Count == 0
                ? []
                : await StockSplitQueries
                    .ForIssuers(_dbContext, issuerIds)
                    .AsNoTracking()
                    .Where(split => split.EffectiveDate <= reportDate)
                    .ToListAsync(cancellationToken);
        var issuersWithSplits = splits.Select(split => split.EquityIssuerId).Distinct().ToList();
        var listings =
            issuersWithSplits.Count == 0
                ? []
                : await _dbContext
                    .Set<EquityListing>()
                    .AsNoTracking()
                    .Where(listing =>
                        listing.MarketCountryCode == "US"
                        && issuersWithSplits.Contains(listing.Security.EquityIssuerId)
                    )
                    .Select(listing => new FundShareRestater.Listing(
                        listing.Security.EquityIssuerId,
                        listing.Id,
                        listing.Ticker,
                        listing.Security.Issuer.Presentation.EquityListingId == listing.Id
                    ))
                    .ToListAsync(cancellationToken);
        return new FundShareRestater(splits, listings);
    }

    // Caps each filer's fund shares at its 13F common shares in the listing for the quarter; a
    // filer with no such position has no row, because the funds are not inside its report.
    private async Task<List<HolderStockAdvisedFundShares>> CapAtThirteenF(
        List<HolderStockAdvisedFundShares> rows,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        if (rows.Count == 0)
            return [];
        var holderIds = rows.Select(row => row.InstitutionalHolderId).Distinct().ToList();
        var positions = await _dbContext
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .Where(holding =>
                holding.ReportDate == reportDate
                && holding.FilingType == FilingType.Form13F
                && holding.ShareType == ShareType.Shares
                && holding.OptionType == null
                && holderIds.Contains(holding.InstitutionalHolderId)
            )
            .GroupBy(holding => new
            {
                holding.InstitutionalHolderId,
                holding.EquityIssuerId,
                holding.ListedTicker,
            })
            .Select(group => new
            {
                group.Key.InstitutionalHolderId,
                group.Key.EquityIssuerId,
                group.Key.ListedTicker,
                Shares = group.Sum(holding => holding.Shares),
            })
            .ToListAsync(cancellationToken);
        var held = positions.ToDictionary(
            position =>
                (position.InstitutionalHolderId, position.EquityIssuerId, position.ListedTicker),
            position => position.Shares
        );

        var capped = new List<HolderStockAdvisedFundShares>(rows.Count);
        foreach (var row in rows)
        {
            if (
                !held.TryGetValue(
                    (row.InstitutionalHolderId, row.EquityIssuerId, row.ListedTicker),
                    out var shares
                )
                || shares <= 0
                || row.FundShares <= 0
            )
                continue;
            row.HolderShares = shares;
            row.IsCapped = row.FundShares > shares;
            row.FundShares = Math.Min(row.FundShares, shares);
            row.IndexFundShares = Math.Min(row.IndexFundShares, row.FundShares);
            capped.Add(row);
        }
        return capped;
    }

    private async Task Publish(
        DateOnly reportDate,
        List<StockQuarterlyFundOwnership> rows,
        List<HolderStockAdvisedFundShares> advised,
        string fingerprint,
        int seriesCount,
        int unclassifiedSeriesCount,
        CancellationToken cancellationToken
    )
    {
        await using (var transaction = await BeginTransaction(cancellationToken))
        {
            // Two workers overlap during a rollout; the second publish waits and then replaces
            // the first instead of colliding with its rows.
            if (transaction != null)
                await _dbContext.Database.ExecuteSqlAsync(
                    $"SELECT pg_advisory_xact_lock({PublishLockKey}, {reportDate.DayNumber})",
                    cancellationToken
                );
            await _dbContext
                .Set<StockQuarterlyFundOwnership>()
                .Where(row => row.ReportDate == reportDate)
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext
                .Set<HolderStockAdvisedFundShares>()
                .Where(row => row.ReportDate == reportDate)
                .ExecuteDeleteAsync(cancellationToken);
            await AddInBatches(rows, cancellationToken);
            await AddInBatches(advised, cancellationToken);

            var state = await _dbContext
                .Set<FundOwnershipQuarterState>()
                .FirstOrDefaultAsync(row => row.ReportDate == reportDate, cancellationToken);
            if (state == null)
            {
                state = new FundOwnershipQuarterState { ReportDate = reportDate };
                _dbContext.Add(state);
            }
            var now = DateTime.UtcNow;
            state.InputFingerprint = fingerprint;
            state.ComputedAt = now;
            state.AttemptedAt = now;
            state.CalculationVersion = CalculationVersion;
            state.SeriesCount = seriesCount;
            state.UnclassifiedSeriesCount = unclassifiedSeriesCount;
            state.FailureCount = 0;
            state.LastError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        _dbContext.ChangeTracker.Clear();
    }

    private async Task AddInBatches<T>(List<T> rows, CancellationToken cancellationToken)
        where T : class
    {
        foreach (var batch in rows.Chunk(PublishBatchSize))
        {
            _dbContext.Set<T>().AddRange(batch);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
        }
    }

    // Test seam for a failure after the rows are built and before they publish.
    protected virtual Task BeforePublish(DateOnly reportDate) => Task.CompletedTask;

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransaction(
        CancellationToken cancellationToken
    ) =>
        _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private sealed record NCenClassification(
        string Key,
        DateOnly ReportEndingPeriod,
        DateOnly FilingDate,
        NCenFundTypes FundTypes,
        bool HasExchangeListing,
        int FundCount
    );
}
