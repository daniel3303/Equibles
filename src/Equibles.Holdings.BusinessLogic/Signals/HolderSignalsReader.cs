using System.Globalization;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Signals.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.Holdings.BusinessLogic.Signals;

/// <summary>
/// Reads a listing's holder signals from the holder-intelligence drain: the newest closed quarter
/// computed with signals, the one before for trends, its holder-overlap peers, and the 13F filers
/// holding it at the highest weight in their books. Every surface reads it, so they agree.
/// </summary>
[Service]
public class HolderSignalsReader
{
    public const int DefaultConvictionHolders = 10;
    public const int MaxConvictionHolders = 50;

    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly HolderSignalsRepository _signalsRepository;
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly EquityIssuerRepository _issuerRepository;
    private readonly IMemoryCache _memoryCache;

    public HolderSignalsReader(
        HolderSignalsRepository signalsRepository,
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        EquityIssuerRepository issuerRepository,
        IMemoryCache memoryCache
    )
    {
        _signalsRepository = signalsRepository;
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
        _issuerRepository = issuerRepository;
        _memoryCache = memoryCache;
    }

    /// <summary>The newest closed quarter computed with signals and when; null before the first.</summary>
    public async Task<(DateOnly ReportDate, DateTime ComputedAt)?> LatestQuarter(
        CancellationToken cancellationToken = default
    )
    {
        var state = await _signalsRepository
            .GetSignalQuarters(HolderCostBasisCalculator.FirstVersion)
            .AsNoTracking()
            .Select(state => new { state.ReportDate, state.ComputedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return state == null ? null : (state.ReportDate, state.ComputedAt.GetValueOrDefault());
    }

    /// <summary>The closed quarters computed with signals, newest first.</summary>
    public Task<List<DateOnly>> Quarters(CancellationToken cancellationToken = default) =>
        _signalsRepository
            .GetSignalQuarters(HolderCostBasisCalculator.FirstVersion)
            .AsNoTracking()
            .Select(state => state.ReportDate)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The listing named by <paramref name="requestedTicker"/>: its primary listing reads the
    /// issuer's primary row, a sibling class or ETF series its own.
    /// </summary>
    public async Task<StockHolderSignalsView> Read(
        EquityIssuer stock,
        string requestedTicker,
        int convictionHolders = DefaultConvictionHolders,
        CancellationToken cancellationToken = default
    )
    {
        var listedTicker = SignalsListing(stock, requestedTicker);
        var latest = await LatestQuarter(cancellationToken);
        if (latest == null)
            return new StockHolderSignalsView
            {
                ListedTicker = listedTicker,
                Unavailable =
                    "Holder signals are not computed yet; the 13F holder-intelligence rebuild has not published a quarter with them.",
            };
        var take = Math.Clamp(convictionHolders, 0, MaxConvictionHolders);
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"holdings:holder-signals:{stock.Id}:{listedTicker}:{latest.Value.ReportDate:O}:{latest.Value.ComputedAt.Ticks}:{take}"
        );
        return await _memoryCache.GetOrCreateAsync(
            key,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await Load(
                    stock,
                    listedTicker,
                    latest.Value.ReportDate,
                    take,
                    cancellationToken
                );
            }
        );
    }

    /// <summary>
    /// The primary listings of one closed quarter ranked by <paramref name="sort"/>; crowding
    /// ranks only listings whose share count divides their holders.
    /// </summary>
    public async Task<List<StockSignalsRankingRow>> RankQuarter(
        DateOnly reportDate,
        StockSignalsSort sort,
        CancellationToken cancellationToken = default
    )
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"holdings:holder-signals-rank:{reportDate:O}:{sort}"
        );
        var state = await _signalsRepository
            .GetSignalQuarters(HolderCostBasisCalculator.FirstVersion)
            .AsNoTracking()
            .Where(state => state.ReportDate == reportDate)
            .Select(state => state.ComputedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (state == null)
            return [];
        return await _memoryCache.GetOrCreateAsync(
            $"{key}:{state.Value.Ticks.ToString(CultureInfo.InvariantCulture)}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var rows = await _signalsRepository
                    .GetPrimaryListings(reportDate)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                var ranked = sort switch
                {
                    StockSignalsSort.Crowding => rows.Where(row =>
                            StockHolderSignalsMath.Crowding(row) != null
                        )
                        .OrderByDescending(row => StockHolderSignalsMath.Crowding(row))
                        .ThenByDescending(row => row.Value)
                        .ThenBy(row => row.EquityIssuerId)
                        .ToList(),
                    _ => rows.OrderByDescending(row => row.ConvictionHolders)
                        .ThenByDescending(row => row.Value)
                        .ThenBy(row => row.EquityIssuerId)
                        .ToList(),
                };
                var names = await IssuerNames(
                    ranked.Select(row => row.EquityIssuerId).ToList(),
                    cancellationToken
                );
                return ranked
                    .Where(row => names.ContainsKey(row.EquityIssuerId))
                    .Select(row => new StockSignalsRankingRow(
                        row,
                        names[row.EquityIssuerId].Ticker,
                        names[row.EquityIssuerId].Name
                    ))
                    .ToList();
            }
        );
    }

    /// <summary>The signal row key of a requested ticker: null for the primary listing.</summary>
    public static string SignalsListing(EquityIssuer stock, string requestedTicker)
    {
        var listed = SecondaryTickerPolicy.ResolveListedTicker(stock, requestedTicker);
        return
            listed == null
            || string.Equals(listed, stock.Presentation?.Listing?.Ticker, StringComparison.Ordinal)
            ? null
            : listed;
    }

    private async Task<StockHolderSignalsView> Load(
        EquityIssuer stock,
        string listedTicker,
        DateOnly latest,
        int convictionHolders,
        CancellationToken cancellationToken
    )
    {
        var rows = await _signalsRepository
            .GetByListing(stock.Id, listedTicker)
            .AsNoTracking()
            .Where(row => row.ReportDate <= latest)
            .OrderByDescending(row => row.ReportDate)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0 || rows[0].ReportDate != latest)
            return new StockHolderSignalsView
            {
                ListedTicker = listedTicker,
                LatestQuarter = latest,
                Unavailable =
                    $"No 13F filer reported holding this listing for {latest:yyyy-MM-dd}, the newest quarter with holder signals.",
            };
        var current = rows[0];
        var previous =
            rows.Count > 1
            && rows[1].ReportDate == HoldingsCorpusCoverage.PreviousQuarterEnd(current.ReportDate)
                ? rows[1]
                : null;

        var overlap = await _signalsRepository
            .GetOverlap(stock.Id, listedTicker)
            .AsNoTracking()
            .Where(row => row.ReportDate <= latest)
            .ToListAsync(cancellationToken);
        var peersAsOf = overlap.Count == 0 ? (DateOnly?)null : overlap.Max(row => row.ReportDate);
        var peerRows = overlap
            .Where(row => row.ReportDate == peersAsOf)
            .OrderBy(row => row.Rank)
            .ToList();
        var names = await IssuerNames(
            peerRows.Select(row => row.PeerEquityIssuerId).Distinct().ToList(),
            cancellationToken
        );
        var peers = peerRows
            .Where(row => names.ContainsKey(row.PeerEquityIssuerId))
            .Select(row => new StockOverlapPeer(
                row.Rank,
                row.PeerEquityIssuerId,
                row.PeerListedTicker ?? names[row.PeerEquityIssuerId].Ticker,
                names[row.PeerEquityIssuerId].Name,
                row.SharedHolders,
                row.Similarity
            ))
            .ToList();

        return new StockHolderSignalsView
        {
            ListedTicker = listedTicker,
            LatestQuarter = latest,
            Current = current,
            Previous = previous,
            PeersAsOf = peersAsOf,
            Peers = peers,
            ConvictionHolders =
                convictionHolders > 0
                    ? await ConvictionHolders(
                        stock,
                        listedTicker,
                        current.ReportDate,
                        convictionHolders,
                        cancellationToken
                    )
                    : [],
        };
    }

    // The listing's 13F filers by the listing's weight in their valued books that quarter.
    private async Task<List<StockConvictionHolder>> ConvictionHolders(
        EquityIssuer stock,
        string listedTicker,
        DateOnly reportDate,
        int take,
        CancellationToken cancellationToken
    )
    {
        var positions = await _holdingRepository
            .Get13FListingValuesAt([stock.Id], reportDate)
            .AsNoTracking()
            .Where(row =>
                listedTicker == null ? row.ListedTicker == null : row.ListedTicker == listedTicker
            )
            .ToListAsync(cancellationToken);
        if (positions.Count == 0)
            return [];
        var holderIds = positions.Select(row => row.InstitutionalHolderId).Distinct().ToList();
        var books = await _holderRepository
            .GetQuarterlyMetrics(reportDate)
            .AsNoTracking()
            .Where(metrics => holderIds.Contains(metrics.InstitutionalHolderId))
            .Select(metrics => new
            {
                metrics.InstitutionalHolderId,
                metrics.EquityAum,
                metrics.StyleBand,
            })
            .ToDictionaryAsync(metrics => metrics.InstitutionalHolderId, cancellationToken);
        var ranked = positions
            .Where(row =>
                row.Value > 0
                && books.TryGetValue(row.InstitutionalHolderId, out var book)
                && book.EquityAum > 0
            )
            .Select(row =>
                (Row: row, Weight: (decimal)row.Value / books[row.InstitutionalHolderId].EquityAum)
            )
            .OrderByDescending(pair => pair.Weight)
            .ThenByDescending(pair => pair.Row.Value)
            .ThenBy(pair => pair.Row.InstitutionalHolderId)
            .Take(take)
            .ToList();
        var rankedIds = ranked.Select(pair => pair.Row.InstitutionalHolderId).ToList();
        var holders = await _holderRepository
            .GetAll()
            .AsNoTracking()
            .Where(holder => rankedIds.Contains(holder.Id))
            .Select(holder => new
            {
                holder.Id,
                holder.Cik,
                holder.Name,
            })
            .ToDictionaryAsync(holder => holder.Id, cancellationToken);
        var types = await _holderRepository.GetEntityTypes(rankedIds, cancellationToken);
        return ranked
            .Where(pair => holders.ContainsKey(pair.Row.InstitutionalHolderId))
            .Select(pair =>
            {
                var holder = holders[pair.Row.InstitutionalHolderId];
                var book = books[pair.Row.InstitutionalHolderId];
                return new StockConvictionHolder
                {
                    HolderId = holder.Id,
                    Cik = holder.Cik,
                    Name = holder.Name,
                    Shares = pair.Row.Shares,
                    Value = pair.Row.Value,
                    Weight = pair.Weight,
                    EquityAum = book.EquityAum,
                    StyleBand = book.StyleBand,
                    EntityType = types.TryGetValue(holder.Id, out var type) ? type : null,
                };
            })
            .ToList();
    }

    internal async Task<Dictionary<Guid, (string Ticker, string Name, Guid? SectorId)>> IssuerNames(
        IReadOnlyCollection<Guid> issuerIds,
        CancellationToken cancellationToken
    )
    {
        var names = new Dictionary<Guid, (string, string, Guid?)>();
        foreach (var chunk in issuerIds.Chunk(5_000))
        {
            var rows = await _issuerRepository
                .GetAll()
                .AsNoTracking()
                .Where(issuer => chunk.Contains(issuer.Id))
                .Select(issuer => new
                {
                    issuer.Id,
                    issuer.Name,
                    issuer.Presentation.Listing.Ticker,
                    issuer.Industry.SectorId,
                })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                names[row.Id] = (row.Ticker, row.Name, row.SectorId);
        }
        return names;
    }
}
