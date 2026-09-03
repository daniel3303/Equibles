using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Rebuilds the materialised <see cref="FundSeries"/> directory from NPORT-P data: one row per
/// registered-fund series, taken from each series' latest report
/// (<see cref="NportFilingRepository.GetLatestPerSeries"/>). The whole directory is recomputed in
/// one pass — funds report monthly and the set of series is small relative to the holdings table,
/// so a periodic full rebuild is simpler than a per-filing dirty-flag drain and never leaves the
/// index stale.
///
/// The rebuild is floor-agnostic: every series ever seen is materialised with its latest report
/// date, and the read side (the commercial /funds index) applies its own data-sync floor by
/// filtering <see cref="FundSeries.LatestReportPeriodDate"/>. Writes go through a single FlexLabs
/// <c>UpsertRange</c> (INSERT … ON CONFLICT (IdentityKey)) and stale rows are pruned by the
/// <see cref="FundSeries.ComputedAt"/> watermark — every row touched this run carries the run's
/// timestamp, so anything older is a series that no longer resolves and is deleted.
/// </summary>
[Service]
public class FundSeriesRefreshService
{
    private const int MaxSlugLength = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FundSeriesRefreshService> _logger;

    public FundSeriesRefreshService(
        IServiceScopeFactory scopeFactory,
        ILogger<FundSeriesRefreshService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public virtual Task RebuildAllAsync(CancellationToken cancellationToken) =>
        RebuildAllAsync(commandTimeout: null, cancellationToken);

    public virtual async Task RebuildAllAsync(
        TimeSpan? commandTimeout,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var nportRepository = scope.ServiceProvider.GetRequiredService<NportFilingRepository>();
        if (commandTimeout is not null)
        {
            dbContext.Database.SetCommandTimeout(commandTimeout.Value);
        }

        // Latest report per series, with the report-header totals and the count of stored holding
        // rows. The joined stock ticker remains the fallback for id-less issuer-feed funds; a
        // series-bearing row is enriched later from SEC's fund-class directory.
        var aggregates = await nportRepository
            .GetLatestPerSeries(DateOnly.MinValue)
            .Select(f => new FundSeriesAggregate
            {
                LatestNportFilingId = f.Id,
                CommonStockId = f.CommonStockId,
                RegistrantCik = f.RegistrantCik,
                SeriesId = f.SeriesId,
                SeriesName = f.SeriesName,
                RegistrantName = f.RegistrantName,
                Ticker = f.CommonStock.Ticker,
                LatestReportPeriodDate = f.ReportPeriodDate,
                LatestFilingDate = f.FilingDate,
                NetAssets = f.NetAssets,
                TotalAssets = f.TotalAssets,
                PositionCount = f.Holdings.Count(),
                ReportedHoldingCount = f.ReportedHoldingCount,
            })
            .ToListAsync(cancellationToken);

        var fundTypeByStock = await LoadFundTypesByStock(dbContext, cancellationToken);
        var tickerDirectory = await LoadSeriesTickers(scope.ServiceProvider, cancellationToken);

        var computedAt = DateTime.UtcNow;
        var rows = aggregates
            .Where(a => a.CommonStockId != null || !string.IsNullOrEmpty(a.RegistrantCik))
            .Select(a => BuildRow(a, fundTypeByStock, tickerDirectory.TickersBySeries, computedAt))
            .ToList();

        if (!tickerDirectory.Available && rows.Count > 0)
        {
            var identityKeys = rows.Select(row => row.IdentityKey).ToList();
            var seriesIds = rows
                .Where(row => !string.IsNullOrEmpty(row.SeriesId))
                .Select(row => row.SeriesId)
                .Distinct()
                .ToList();
            var existingTickerMetadata = await dbContext
                .Set<FundSeries>()
                .Where(row =>
                    identityKeys.Contains(row.IdentityKey)
                    || (!string.IsNullOrEmpty(row.SeriesId) && seriesIds.Contains(row.SeriesId))
                )
                .Select(row => new
                {
                    row.IdentityKey,
                    row.SeriesId,
                    row.Ticker,
                    row.ClassTickers,
                })
                .ToListAsync(cancellationToken);
            var metadataByIdentity = existingTickerMetadata.ToDictionary(row => row.IdentityKey);
            var metadataBySeries = existingTickerMetadata
                .Where(row => !string.IsNullOrEmpty(row.SeriesId))
                .GroupBy(row => row.SeriesId, StringComparer.OrdinalIgnoreCase)
                .Where(group =>
                    group.Select(row => TickerMetadataKey(row.Ticker, row.ClassTickers))
                        .Distinct(StringComparer.Ordinal)
                        .Count() == 1
                )
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (!string.IsNullOrEmpty(row.SeriesId))
                {
                    if (metadataBySeries.TryGetValue(row.SeriesId, out var existingSeries))
                    {
                        row.Ticker = existingSeries.Ticker;
                        row.ClassTickers = existingSeries.ClassTickers;
                    }
                    continue;
                }

                if (metadataByIdentity.TryGetValue(row.IdentityKey, out var existingIdentity))
                {
                    row.Ticker = existingIdentity.Ticker;
                    row.ClassTickers = existingIdentity.ClassTickers;
                }
            }
        }

        _logger.LogInformation("Rebuilding fund-series directory: {Count} series", rows.Count);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken
        );

        if (rows.Count > 0)
        {
            // Reprocessing can change a filing's resolved identity while retaining its filing ID,
            // and a series can move populations while retaining its route slug. Remove either
            // superseded owner before upsert so both unique indexes permit the replacement; the
            // transaction preserves the old directory if the subsequent rebuild fails.
            var currentIdentityKeys = rows.Select(s => s.IdentityKey).ToList();
            var currentSlugs = rows.Select(s => s.Slug).ToList();
            var currentFilingIds = rows.Select(s => s.LatestNportFilingId).ToList();
            await dbContext
                .Set<FundSeries>()
                .Where(s =>
                    !currentIdentityKeys.Contains(s.IdentityKey)
                    && (
                        currentSlugs.Contains(s.Slug)
                        || currentFilingIds.Contains(s.LatestNportFilingId)
                    )
                )
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext
                .Set<FundSeries>()
                .UpsertRange(rows)
                .On(s => s.IdentityKey)
                .WhenMatched(
                    (_, incoming) =>
                        new FundSeries
                        {
                            Slug = incoming.Slug,
                            CommonStockId = incoming.CommonStockId,
                            RegistrantCik = incoming.RegistrantCik,
                            SeriesId = incoming.SeriesId,
                            SeriesName = incoming.SeriesName,
                            RegistrantName = incoming.RegistrantName,
                            Ticker = incoming.Ticker,
                            ClassTickers = incoming.ClassTickers,
                            LatestNportFilingId = incoming.LatestNportFilingId,
                            LatestReportPeriodDate = incoming.LatestReportPeriodDate,
                            LatestFilingDate = incoming.LatestFilingDate,
                            NetAssets = incoming.NetAssets,
                            TotalAssets = incoming.TotalAssets,
                            PositionCount = incoming.PositionCount,
                            ReportedHoldingCount = incoming.ReportedHoldingCount,
                            FundType = incoming.FundType,
                            ComputedAt = incoming.ComputedAt,
                        }
                )
                .RunAsync(cancellationToken);
        }

        // Series no longer present (renamed away, dropped from the floor, deleted) keep their older
        // ComputedAt and are pruned. With no series at all this deletes the whole table.
        var deleted = await dbContext
            .Set<FundSeries>()
            .Where(s => s.ComputedAt < computedAt)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            _logger.LogInformation("Pruned {Count} stale fund-series rows", deleted);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Latest N-CEN registration type per tracked fund. N-CEN is filed only by tracked funds
    // (keyed by CommonStockId), so trusts get no type. The table is small (annual filings), so it
    // is read whole and folded in memory.
    private static async Task<Dictionary<Guid, string>> LoadFundTypesByStock(
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var ncen = await dbContext
            .Set<NCenFiling>()
            .Select(n => new
            {
                n.CommonStockId,
                n.FilingDate,
                n.InvestmentCompanyType,
            })
            .ToListAsync(cancellationToken);

        return ncen.GroupBy(n => n.CommonStockId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(n => n.FilingDate).First().InvestmentCompanyType
            );
    }

    // Series → every exact share-class symbol from SEC's fund-class ticker directory. Ticker keeps
    // its historical single-symbol meaning; ClassTickers preserves multi-class ETF/mutual series.
    // Best-effort: a directory fetch error degrades to no enrichment rather than failing rebuild.
    private async Task<SeriesTickerDirectory> LoadSeriesTickers(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var edgarClient = services.GetRequiredService<ISecEdgarClient>();
            var classTickers = await edgarClient.GetFundClassTickers();
            cancellationToken.ThrowIfCancellationRequested();
            if (classTickers.Count == 0)
            {
                _logger.LogWarning(
                    "Fund-class ticker directory was empty; preserving stored ticker metadata"
                );
                return new SeriesTickerDirectory([], false);
            }
            var tickerMap = BuildSeriesTickerMap(classTickers);
            if (tickerMap.Count == 0)
            {
                _logger.LogWarning(
                    "Fund-class ticker directory contained no unambiguous tickers; preserving stored ticker metadata"
                );
                return new SeriesTickerDirectory([], false);
            }
            return new SeriesTickerDirectory(tickerMap, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Fund-class ticker directory unavailable; rebuilding without ticker enrichment"
            );
            return new SeriesTickerDirectory([], false);
        }
    }

    internal static Dictionary<string, List<string>> BuildSeriesTickerMap(
        List<FundClassTicker> classTickers
    )
    {
        var normalized = classTickers
            .Select(t => new
            {
                t.SeriesId,
                Symbol = TickerNormalizer.NormalizeDashListed(t.Symbol),
            })
            .Where(t => t.Symbol != null && !string.IsNullOrWhiteSpace(t.SeriesId))
            .ToList();
        var unambiguous = normalized
            .GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase)
            .Where(group =>
                group.Select(t => t.SeriesId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() == 1
            )
            .SelectMany(group => group);
        return unambiguous
            .GroupBy(t => t.SeriesId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => t.Symbol)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(ticker => ticker, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    private sealed record SeriesTickerDirectory(
        Dictionary<string, List<string>> TickersBySeries,
        bool Available
    );

    private static string TickerMetadataKey(string ticker, IEnumerable<string> classTickers)
    {
        var normalizedClasses = (classTickers ?? [])
            .Select(TickerNormalizer.NormalizeDashListed)
            .Where(value => value != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal);
        return $"{TickerNormalizer.NormalizeDashListed(ticker)}\0{string.Join('\0', normalizedClasses)}";
    }

    private static FundSeries BuildRow(
        FundSeriesAggregate a,
        Dictionary<Guid, string> fundTypeByStock,
        Dictionary<string, List<string>> tickersBySeries,
        DateTime computedAt
    )
    {
        var seriesId = a.SeriesId ?? string.Empty;
        var isTracked = a.CommonStockId != null;

        var identityKey = isTracked
            ? BuildTrackedIdentityKey(a.CommonStockId.Value, seriesId)
            : $"rc:{a.RegistrantCik}:{seriesId}";

        var displayName = !string.IsNullOrWhiteSpace(a.SeriesName)
            ? a.SeriesName
            : a.RegistrantName;

        var discriminator =
            !string.IsNullOrEmpty(seriesId) ? seriesId
            : isTracked ? (!string.IsNullOrEmpty(a.Ticker) ? a.Ticker : a.CommonStockId.ToString())
            : $"cik{a.RegistrantCik}";

        string fundType = null;
        if (isTracked)
        {
            fundTypeByStock.TryGetValue(a.CommonStockId.Value, out fundType);
        }

        // A series-bearing filing can share its issuer-feed stock with hundreds of sibling series,
        // so that stock's ticker is not series authority. SEC's fund-class directory is.
        string ticker = null;
        List<string> classTickers = [];
        if (!string.IsNullOrEmpty(seriesId))
        {
            tickersBySeries.TryGetValue(seriesId, out classTickers);
            classTickers ??= [];
            ticker = classTickers.Count == 1 ? classTickers[0] : null;
        }
        else if (isTracked)
        {
            ticker = a.Ticker;
            var normalized = TickerNormalizer.NormalizeDashListed(a.Ticker);
            classTickers = normalized == null ? [] : [normalized];
        }

        return new FundSeries
        {
            IdentityKey = identityKey,
            Slug = BuildSlug(displayName, discriminator),
            CommonStockId = a.CommonStockId,
            RegistrantCik = a.RegistrantCik,
            SeriesId = seriesId,
            SeriesName = a.SeriesName,
            RegistrantName = a.RegistrantName,
            Ticker = ticker,
            ClassTickers = classTickers,
            LatestNportFilingId = a.LatestNportFilingId,
            LatestReportPeriodDate = a.LatestReportPeriodDate,
            LatestFilingDate = a.LatestFilingDate,
            NetAssets = a.NetAssets,
            TotalAssets = a.TotalAssets,
            PositionCount = a.PositionCount,
            ReportedHoldingCount = a.ReportedHoldingCount,
            FundType = fundType,
            ComputedAt = computedAt,
        };
    }

    internal static string BuildTrackedIdentityKey(Guid commonStockId, string seriesId) =>
        string.IsNullOrEmpty(seriesId) ? $"cs:{commonStockId}" : $"cs:{commonStockId}:{seriesId}";

    // "{name-slug}-{discriminator}". The discriminator (unique per series) is preserved whole; only
    // the name part is trimmed to fit, so the slug stays unique under truncation.
    private static string BuildSlug(string name, string discriminator)
    {
        var disc = Slugify(discriminator);
        var baseSlug = Slugify(name);

        var room = MaxSlugLength - disc.Length - 1;
        if (room <= 0)
        {
            return disc.Length > MaxSlugLength ? disc[..MaxSlugLength] : disc;
        }
        if (baseSlug.Length > room)
        {
            baseSlug = baseSlug[..room].TrimEnd('-');
        }
        return string.IsNullOrEmpty(baseSlug) ? disc : $"{baseSlug}-{disc}";
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingDash = false;
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                pendingDash = false;
            }
            else if (!pendingDash && builder.Length > 0)
            {
                builder.Append('-');
                pendingDash = true;
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    private class FundSeriesAggregate
    {
        public Guid LatestNportFilingId { get; set; }
        public Guid? CommonStockId { get; set; }
        public string RegistrantCik { get; set; }
        public string SeriesId { get; set; }
        public string SeriesName { get; set; }
        public string RegistrantName { get; set; }
        public string Ticker { get; set; }
        public DateOnly LatestReportPeriodDate { get; set; }
        public DateOnly LatestFilingDate { get; set; }
        public decimal NetAssets { get; set; }
        public decimal TotalAssets { get; set; }
        public int PositionCount { get; set; }
        public int? ReportedHoldingCount { get; set; }
    }
}
