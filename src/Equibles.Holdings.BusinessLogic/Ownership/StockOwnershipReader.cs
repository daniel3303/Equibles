using System.Globalization;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Links;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// Reads a listing's unified holder list (Bloomberg HDS style) and ownership summary at request
/// time from every registered <see cref="IHolderSourceContributor"/>, never from a materialized
/// table. Each lane is a stock-scoped read; the summary and the first default-view rows are cached
/// per listing, keyed on the 13F snapshot state and the holder links' refresh.
/// </summary>
public class StockOwnershipReader
{
    /// <summary>How many default-view rows a cached read keeps.</summary>
    public const int CachedDefaultRows = 500;

    // Children kept per cached top row; the full read has them all.
    private const int CachedChildrenPerRow = 25;

    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly IEnumerable<IHolderSourceContributor> _contributors;
    private readonly IBeneficialOwnershipPurposeSource _purposeSource;
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly StockCombinedQuarterService _combinedQuarterService;
    private readonly IHolderRollupReader _rollupReader;
    private readonly ISharesOutstandingProvider _sharesOutstandingProvider;
    private readonly IMemoryCache _memoryCache;
    private readonly TimeProvider _timeProvider;

    public StockOwnershipReader(
        IEnumerable<IHolderSourceContributor> contributors,
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        StockSplitRepository stockSplitRepository,
        StockCombinedQuarterService combinedQuarterService,
        IHolderRollupReader rollupReader,
        IMemoryCache memoryCache,
        ISharesOutstandingProvider sharesOutstandingProvider = null,
        TimeProvider timeProvider = null,
        IBeneficialOwnershipPurposeSource purposeSource = null
    )
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _purposeSource = purposeSource;
        _contributors = contributors;
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
        _stockSplitRepository = stockSplitRepository;
        _combinedQuarterService = combinedQuarterService;
        _rollupReader = rollupReader;
        _memoryCache = memoryCache;
        _sharesOutstandingProvider = sharesOutstandingProvider;
    }

    /// <summary>
    /// The listing's holders and summary. Unless <paramref name="complete"/>, a cached read holding
    /// only the summary and the first <see cref="CachedDefaultRows"/> default-view rows may answer.
    /// </summary>
    public async Task<StockOwnershipRead> Read(
        EquityIssuer stock,
        string requestedTicker,
        bool complete = false,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var listedTicker =
            SecondaryTickerPolicy.ResolveListedTicker(stock, requestedTicker)
            ?? stock.Presentation.Listing.Ticker;
        var exactListing = SecondaryTickerPolicy.RequiresExactListingScope(stock, listedTicker);
        var anchor = await ResolveAnchor(
            stock,
            listedTicker,
            exactListing,
            today,
            cancellationToken
        );

        var snapshot =
            anchor == null
                ? null
                : await _holdingRepository.GetMarketActivitySnapshotState(
                    anchor.ReportDate,
                    anchor.PreviousReportDate ?? anchor.ReportDate,
                    anchor.IsCombined,
                    cancellationToken
                );
        var linksComputedAt = await _rollupReader.GetComputedAt(cancellationToken);
        // A dirty quarter is mid-rebuild, so its read is neither served from nor stored in the cache.
        var cacheable = snapshot is not { IsDirty: true };
        var key = CacheKey(stock.Id, listedTicker, anchor, snapshot, linksComputedAt);
        if (!complete && cacheable && _memoryCache.TryGetValue(key, out StockOwnershipRead cached))
            return cached;

        var splits = await _stockSplitRepository
            .GetEffectiveByStock(stock.Id, today)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var scope = new StockOwnershipScope
        {
            Stock = stock,
            ListedTicker = listedTicker,
            ExactListing = exactListing,
            Anchor = anchor,
            Splits = splits,
            Today = today,
        };
        var read = await ReadComplete(scope, cancellationToken);
        if (cacheable)
            _memoryCache.Set(key, Trim(read), CacheDuration);
        return read;
    }

    /// <summary>
    /// The cache key: listing, presented quarter, 13F snapshot generation and links refresh,
    /// formatted culture-invariantly so request and background threads share entries.
    /// </summary>
    internal static string CacheKey(
        Guid stockId,
        string listedTicker,
        StockQuarterAnchor anchor,
        HoldingsSnapshotState snapshot,
        DateTime? linksComputedAt
    ) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"stock-ownership:v1:{stockId:N}:{listedTicker}:{anchor?.ReportDate.ToString("O", CultureInfo.InvariantCulture)}:{anchor?.IsCombined}:{snapshot?.ComputedAt?.ToString("O", CultureInfo.InvariantCulture)}:{linksComputedAt?.ToString("O", CultureInfo.InvariantCulture)}"
        );

    private async Task<StockOwnershipRead> ReadComplete(
        StockOwnershipScope scope,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<StockHolderRow>();
        foreach (var contributor in _contributors)
            rows.AddRange(await contributor.Read(scope, cancellationToken));
        await DescribePurposes(
            rows.Where(row => row.Source == StockHolderSource.Schedule13D).ToList(),
            row => row.Accession,
            (row, purpose) => row.Purpose = purpose,
            _purposeSource,
            cancellationToken
        );

        var (sharesOutstanding, percentUnavailable) = await SharesOutstanding(
            scope,
            cancellationToken
        );
        foreach (var row in rows)
            row.PercentOutstanding =
                percentUnavailable == null
                && sharesOutstanding is > 0
                && !row.Has(StockHolderRowFlags.SplitUnresolved)
                    ? Math.Round(100m * row.Shares / sharesOutstanding.Value, 4)
                    : null;

        var holderIds = rows.Select(row => row.InstitutionalHolderId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var styleAsOf = scope.Anchor is { } anchor
            ? await _holderRepository.GetLatestMetricsReportDate(
                anchor.ReportDate,
                cancellationToken
            )
            : null;
        var mixHolders = await LoadMixHolders(
            rows.Where(row => row.Source == StockHolderSource.Form13F)
                .Select(row => row.InstitutionalHolderId)
                .OfType<Guid>()
                .Distinct()
                .ToList(),
            styleAsOf,
            cancellationToken
        );
        var entityTypes = await _holderRepository.GetEntityTypes(holderIds, cancellationToken);
        var styleBands = styleAsOf is { } measured
            ? await _holderRepository.GetStyleBands(holderIds, measured, cancellationToken)
            : new Dictionary<Guid, HolderStyleBand>();
        foreach (var row in rows.Where(row => row.InstitutionalHolderId != null))
        {
            var id = row.InstitutionalHolderId!.Value;
            row.EntityType ??= entityTypes.TryGetValue(id, out var type) ? type : null;
            row.StyleBand ??= styleBands.TryGetValue(id, out var band) ? band : null;
        }

        var rollups = await _rollupReader.GetRollups(
            rows.Select(row => row.Cik).OfType<string>().Distinct(),
            cancellationToken
        );
        foreach (var row in rows)
            row.RollupCik = UltimateAggregation.UltimateOf(row, rollups);
        var filedOwn = await FiledOwn13F(rows, rollups, cancellationToken);
        var tops = StockOwnershipPrecedence.Arrange(
            rows,
            new StockOwnershipPrecedence.Context
            {
                Rollups = rollups,
                FiledOwn13F = (cik, quarter) => filedOwn.Contains((cik, quarter)),
                Today = scope.Today,
            }
        );

        var multiClass = !scope.ExactListing && await IsMultiClass(scope.Stock, cancellationToken);
        var summary = OwnershipSummaryCalculator.Build(
            rows,
            new OwnershipSummaryCalculator.Context
            {
                SharesOutstanding = sharesOutstanding,
                PercentUnavailableReason = percentUnavailable,
                InsidersNotSummedReason = multiClass
                    ? "the issuer has more than one class of common stock and Forms 3, 4 and 5 report one issuer-wide balance across them"
                    : null,
                InstitutionalAsOf = scope.Anchor?.ReportDate,
                InstitutionalCombined = scope.Anchor?.IsCombined == true,
                MixHolder = id => mixHolders.GetValueOrDefault(id),
                StyleAsOf = styleAsOf,
            }
        );
        var names = await UltimateNames(rollups, rows, cancellationToken);
        var ultimate = UltimateAggregation.Build(
            rows,
            rollups,
            cik => names.GetValueOrDefault(cik),
            percentUnavailable == null ? sharesOutstanding : null
        );
        var visible = StockOwnershipPrecedence.Visible(tops);
        return new StockOwnershipRead
        {
            ListedTicker = scope.ListedTicker,
            ExactListing = scope.ExactListing,
            Anchor = scope.Anchor,
            Summary = summary,
            DefaultRows = visible,
            DefaultRowCount = visible.Count,
            Rows = StockOwnershipPrecedence.Ordered(rows).ToList(),
            UltimateGroups = ultimate,
            Complete = true,
        };
    }

    private static StockOwnershipRead Trim(StockOwnershipRead read) =>
        new()
        {
            ListedTicker = read.ListedTicker,
            ExactListing = read.ExactListing,
            Anchor = read.Anchor,
            Summary = read.Summary,
            DefaultRows = read
                .DefaultRows.Take(CachedDefaultRows)
                .Select(row => row.WithFirstChildren(CachedChildrenPerRow))
                .ToList(),
            DefaultRowCount = read.DefaultRowCount,
            Complete = false,
        };

    // The primary stock presents its open quarter as the combined view; an exact listing (an ETF
    // series or a sibling class) reads its newest quarter as filed, since carry-forward is filer-wide.
    private async Task<StockQuarterAnchor> ResolveAnchor(
        EquityIssuer stock,
        string listedTicker,
        bool exactListing,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        if (!exactListing)
            return await _combinedQuarterService.Resolve(stock, today, cancellationToken);
        var dates = await _holdingRepository.Get13FReportDatesByListingSnapshotBacked(
            stock,
            listedTicker,
            cancellationToken
        );
        return dates.Count == 0
            ? null
            : new StockQuarterAnchor
            {
                ReportDate = dates[0],
                PreviousReportDate = dates.Count > 1 ? dates[1] : null,
                FilingWindowOpen = false,
            };
    }

    /// <summary>Sets each 13D's Item 4 purpose from the deployment's source, when it has one.</summary>
    internal static async Task DescribePurposes<T>(
        IReadOnlyList<T> schedules13D,
        Func<T, string> accessionOf,
        Action<T, string> setPurpose,
        IBeneficialOwnershipPurposeSource purposeSource,
        CancellationToken cancellationToken
    )
    {
        var accessions = schedules13D
            .Select(accessionOf)
            .Where(accession => accession != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (purposeSource == null || accessions.Count == 0)
            return;
        var purposes = await purposeSource.Describe(accessions, cancellationToken);
        foreach (var schedule in schedules13D)
            if (
                accessionOf(schedule) is { } accession
                && purposes.TryGetValue(accession, out var purpose)
            )
                setPurpose(schedule, purpose);
    }

    private async Task<(long? Shares, string Unavailable)> SharesOutstanding(
        StockOwnershipScope scope,
        CancellationToken cancellationToken
    )
    {
        if (scope.ExactListing)
            return (null, "shares outstanding is an issuer-wide count, not one listing's");
        if (_sharesOutstandingProvider == null)
            return (null, "no shares outstanding source is configured");
        var shares = await _sharesOutstandingProvider.GetCurrentSharesOutstanding(
            scope.Stock,
            cancellationToken
        );
        if (shares is not > 0)
            return (null, "no shares outstanding count is on record");
        if (await _sharesOutstandingProvider.IsForeignPrivateIssuer(scope.Stock, cancellationToken))
            return (
                null,
                "a foreign private issuer reports ordinary shares, a different unit from the listed shares"
            );
        return (shares, null);
    }

    // A second class of common stock, seen in the issuer's 12(b) registrations or its per-class
    // cover-page counts, means Forms 3 to 5 balances cannot be summed against one class.
    private async Task<bool> IsMultiClass(EquityIssuer stock, CancellationToken cancellationToken)
    {
        var registeredClasses = stock.Securities.Count(security =>
            security.RegistrationType == ListedSecurityType.CommonShares
            && security.Listings.Any(listing => listing.MarketCountryCode == "US" && listing.Active)
        );
        if (registeredClasses > 1)
            return true;
        return _sharesOutstandingProvider != null
            && await _sharesOutstandingProvider.GetSummedPerClassSharesOutstanding(
                stock,
                cancellationToken
            )
                is > 0;
    }

    // The (CIK, quarter) pairs where a holder a rollup would nest under a 13F filer of the listing
    // filed its own 13F for that filer's quarter: an edge also counts the quarter after its own, so
    // a child that has since filed a full report is its own holder.
    private async Task<HashSet<(string, DateOnly)>> FiledOwn13F(
        IReadOnlyList<StockHolderRow> rows,
        IReadOnlyDictionary<string, HolderRollup> rollups,
        CancellationToken cancellationToken
    )
    {
        var quarterOf13FCik = rows.Where(row =>
                row.Source == StockHolderSource.Form13F && row.Cik != null
            )
            .GroupBy(row => row.Cik)
            .ToDictionary(group => group.Key, group => group.First().AsOf, StringComparer.Ordinal);
        var candidates = rows.Where(row =>
                row.Cik != null
                && row.Source != StockHolderSource.Form13F
                && !quarterOf13FCik.ContainsKey(row.Cik)
                && rollups.ContainsKey(row.Cik)
            )
            .Select(row =>
            {
                var parent = StockOwnershipPrecedence
                    .ReportingParentCandidates(rollups[row.Cik])
                    .FirstOrDefault(cik => cik != row.Cik && quarterOf13FCik.ContainsKey(cik));
                return parent == null ? default : (Cik: row.Cik, Quarter: quarterOf13FCik[parent]);
            })
            .Where(candidate => candidate.Cik != null)
            .Distinct()
            .ToList();
        var filed = new HashSet<(string, DateOnly)>();
        if (candidates.Count == 0)
            return filed;
        var holders = await _holderRepository.GetByCiks(
            candidates.Select(candidate => candidate.Cik).Distinct(),
            cancellationToken
        );
        var idByCik = holders
            .Where(holder => CikNormalizer.Canonicalize(holder.Cik) != null)
            .ToDictionary(
                holder => CikNormalizer.Canonicalize(holder.Cik),
                holder => holder.Id,
                StringComparer.Ordinal
            );
        foreach (var quarter in candidates.GroupBy(candidate => candidate.Quarter))
        {
            var ids = quarter
                .Select(candidate =>
                    idByCik.TryGetValue(candidate.Cik, out var id) ? id : (Guid?)null
                )
                .OfType<Guid>()
                .ToList();
            if (ids.Count == 0)
                continue;
            var filers = await _holdingRepository
                .GetFiledHolderIdsAmong(quarter.Key, ids)
                .ToListAsync(cancellationToken);
            filed.UnionWith(
                quarter
                    .Where(candidate =>
                        idByCik.TryGetValue(candidate.Cik, out var id) && filers.Contains(id)
                    )
                    .Select(candidate => (candidate.Cik, quarter.Key))
            );
        }
        return filed;
    }

    private async Task<Dictionary<Guid, HolderMixHolder>> LoadMixHolders(
        List<Guid> holderIds,
        DateOnly? measured,
        CancellationToken cancellationToken
    )
    {
        if (holderIds.Count == 0)
            return [];
        var profiles = await _holderRepository
            .GetProfiles(holderIds)
            .AsNoTracking()
            .Select(profile => new
            {
                profile.InstitutionalHolderId,
                profile.EntityType,
                profile.ClientBase,
                profile.AdvisesPrivateFunds,
                profile.PrivateFundFocus,
            })
            .ToDictionaryAsync(profile => profile.InstitutionalHolderId, cancellationToken);
        var bands = measured is { } quarter
            ? await _holderRepository.GetStyleBands(holderIds, quarter, cancellationToken)
            : new Dictionary<Guid, HolderStyleBand>();
        var locations = await _holderRepository
            .GetAll()
            .AsNoTracking()
            .Where(holder => holderIds.Contains(holder.Id))
            .Select(holder => new { holder.Id, holder.StateOrCountry })
            .ToDictionaryAsync(
                holder => holder.Id,
                holder => holder.StateOrCountry,
                cancellationToken
            );
        return holderIds.ToDictionary(
            id => id,
            id =>
            {
                var profile = profiles.GetValueOrDefault(id);
                return HolderMixHolder.Of(
                    profile == null
                        ? null
                        : new HolderMixProfile(
                            profile.EntityType,
                            profile.ClientBase,
                            profile.AdvisesPrivateFunds,
                            profile.PrivateFundFocus
                        ),
                    bands.TryGetValue(id, out var band) ? band : null,
                    null,
                    null,
                    null,
                    locations.GetValueOrDefault(id)
                );
            }
        );
    }

    // The names of ultimate parents with no row of their own in the listing.
    private async Task<Dictionary<string, string>> UltimateNames(
        IReadOnlyDictionary<string, HolderRollup> rollups,
        IReadOnlyList<StockHolderRow> rows,
        CancellationToken cancellationToken
    )
    {
        var present = rows.Select(row => row.Cik)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var missing = rollups
            .Values.Select(rollup => rollup.UltimateCik)
            .Where(cik => cik != null && !present.Contains(cik))
            .Distinct()
            .ToList();
        if (missing.Count == 0)
            return [];
        var holders = await _holderRepository.GetByCiks(missing, cancellationToken);
        return holders
            .Where(holder => CikNormalizer.Canonicalize(holder.Cik) != null)
            .GroupBy(holder => CikNormalizer.Canonicalize(holder.Cik))
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
    }
}
