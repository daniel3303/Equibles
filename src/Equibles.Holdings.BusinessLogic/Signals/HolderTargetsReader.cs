using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Signals.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.Holdings.BusinessLogic.Signals;

/// <summary>
/// Shareholder targeting: the 13F filers of the newest closed quarter that hold a listing's
/// holder-overlap peers (or, without peers, its sector's largest 13F holdings) but no class of the
/// stock itself, ranked by the money they have in those peers. Computed at read time from the
/// overlap rows, the presence masks and one bounded 13F read of the peers.
/// </summary>
[Service]
public class HolderTargetsReader
{
    public const int DefaultMaxResults = 25;

    /// <summary>The peers a sector fallback reads: the sector's largest 13F holdings.</summary>
    public const int SectorPeers = HolderOverlapCalculator.PeersPerSecurity;

    private const int TopPeersShown = 3;

    private readonly HolderSignalsRepository _signalsRepository;
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly SectorRepository _sectorRepository;
    private readonly HolderSignalsReader _signalsReader;
    private readonly IMemoryCache _memoryCache;

    public HolderTargetsReader(
        HolderSignalsRepository signalsRepository,
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        SectorRepository sectorRepository,
        HolderSignalsReader signalsReader,
        IMemoryCache memoryCache
    )
    {
        _signalsRepository = signalsRepository;
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
        _sectorRepository = sectorRepository;
        _signalsReader = signalsReader;
        _memoryCache = memoryCache;
    }

    public async Task<StockHolderTargets> Read(
        EquityIssuer stock,
        string requestedTicker,
        StockHolderTargetSort sort = StockHolderTargetSort.PeerValue,
        int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default
    )
    {
        var latest = await _signalsReader.LatestQuarter(cancellationToken);
        if (latest == null)
            return new StockHolderTargets
            {
                Sort = sort,
                Unavailable =
                    "Shareholder targets are not computed yet; the 13F holder-intelligence rebuild has not published holder-overlap peers.",
            };
        var listedTicker = HolderSignalsReader.SignalsListing(stock, requestedTicker);
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"holdings:holder-targets:{stock.Id}:{listedTicker}:{latest.Value.ReportDate:O}:{latest.Value.ComputedAt.Ticks}:{sort}"
        );
        var all = await _memoryCache.GetOrCreateAsync(
            key,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = HolderSignalsReader.CacheDuration;
                return await Load(
                    stock,
                    listedTicker,
                    latest.Value.ReportDate,
                    sort,
                    cancellationToken
                );
            }
        );
        return all with { Targets = all.Targets.Take(Math.Clamp(maxResults, 1, 500)).ToList() };
    }

    private async Task<StockHolderTargets> Load(
        EquityIssuer stock,
        string listedTicker,
        DateOnly latest,
        StockHolderTargetSort sort,
        CancellationToken cancellationToken
    )
    {
        var overlap = await _signalsRepository
            .GetOverlap(stock.Id, listedTicker)
            .AsNoTracking()
            .Where(row => row.ReportDate <= latest)
            .ToListAsync(cancellationToken);
        var reportDate = overlap.Count > 0 ? overlap.Max(row => row.ReportDate) : latest;
        var peerKeys = overlap
            .Where(row => row.ReportDate == reportDate)
            .OrderBy(row => row.Rank)
            .Select(row =>
                (
                    Issuer: row.PeerEquityIssuerId,
                    Ticker: row.PeerListedTicker,
                    row.SharedHolders,
                    row.Similarity
                )
            )
            .ToList();

        string sectorName = null;
        var sectorFallback = peerKeys.Count == 0;
        if (sectorFallback)
        {
            var sectorId = (await _signalsReader.IssuerNames([stock.Id], cancellationToken))
                .GetValueOrDefault(stock.Id)
                .SectorId;
            if (sectorId == null)
                return new StockHolderTargets
                {
                    ReportDate = reportDate,
                    Sort = sort,
                    Unavailable =
                        "This listing has no holder-overlap peers among active filers and no sector, so there are no peers to find targets through.",
                };
            sectorName = await _sectorRepository
                .GetAll()
                .AsNoTracking()
                .Where(sector => sector.Id == sectorId)
                .Select(sector => sector.Name)
                .FirstOrDefaultAsync(cancellationToken);
            var candidates = await _signalsRepository
                .GetPrimaryListings(reportDate)
                .AsNoTracking()
                .Where(row => row.EquityIssuerId != stock.Id)
                .OrderByDescending(row => row.Value)
                .Select(row => new { row.EquityIssuerId, row.Value })
                .Take(SectorPeers * 20)
                .ToListAsync(cancellationToken);
            var candidateNames = await _signalsReader.IssuerNames(
                candidates.Select(row => row.EquityIssuerId).ToList(),
                cancellationToken
            );
            peerKeys = candidates
                .Where(row =>
                    candidateNames.TryGetValue(row.EquityIssuerId, out var name)
                    && name.SectorId == sectorId
                )
                .Take(SectorPeers)
                .Select(row => (row.EquityIssuerId, (string)null, 0, 0m))
                .ToList();
            if (peerKeys.Count == 0)
                return new StockHolderTargets
                {
                    ReportDate = reportDate,
                    Sort = sort,
                    SectorFallback = true,
                    SectorName = sectorName,
                    Unavailable =
                        $"No other {sectorName} stock was held by 13F filers for {reportDate:yyyy-MM-dd}.",
                };
        }

        var names = await _signalsReader.IssuerNames(
            peerKeys.Select(peer => peer.Issuer).Distinct().ToList(),
            cancellationToken
        );
        var peers = peerKeys
            .Where(peer => names.ContainsKey(peer.Issuer))
            .Select(
                (peer, index) =>
                    new StockOverlapPeer(
                        index + 1,
                        peer.Issuer,
                        peer.Ticker ?? names[peer.Issuer].Ticker,
                        names[peer.Issuer].Name,
                        peer.SharedHolders,
                        peer.Similarity
                    )
            )
            .ToList();
        var peerLabels = peerKeys
            .Where(peer => names.ContainsKey(peer.Issuer))
            .ToDictionary(
                peer => (peer.Issuer, peer.Ticker),
                peer => peer.Ticker ?? names[peer.Issuer].Ticker
            );

        var quarter = HolderTenureCalculator.QuarterIndex(reportDate);
        var holdersOfStock =
            quarter == null
                ? new HashSet<Guid>()
                : (
                    await _signalsRepository
                        .GetHoldersOfIssuer(stock.Id, 1L << quarter.Value)
                        .ToListAsync(cancellationToken)
                ).ToHashSet();
        var positions = await _holdingRepository
            .Get13FListingValuesAt(
                peerLabels.Keys.Select(peer => peer.Issuer).Distinct().ToList(),
                reportDate
            )
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var byHolder = positions
            .Where(row =>
                row.Value > 0
                && peerLabels.ContainsKey((row.EquityIssuerId, row.ListedTicker))
                && !holdersOfStock.Contains(row.InstitutionalHolderId)
            )
            .GroupBy(row => row.InstitutionalHolderId)
            .ToList();
        if (byHolder.Count == 0)
            return new StockHolderTargets
            {
                ReportDate = reportDate,
                Sort = sort,
                SectorFallback = sectorFallback,
                SectorName = sectorName,
                Peers = peers,
                Unavailable = "Every 13F filer holding these peers also holds the stock.",
            };

        var holderIds = byHolder.Select(group => group.Key).ToList();
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
        var scored = byHolder
            .Where(group => books.TryGetValue(group.Key, out var book) && book.EquityAum > 0)
            .Select(group =>
            {
                var book = books[group.Key];
                var peerValue = group.Sum(row => row.Value);
                return (
                    HolderId: group.Key,
                    PeerValue: peerValue,
                    PeerWeight: Math.Min(1m, (decimal)peerValue / book.EquityAum),
                    book.EquityAum,
                    book.StyleBand,
                    PeersHeld: group.Count(),
                    TopPeers: group
                        .OrderByDescending(row => row.Value)
                        .Take(TopPeersShown)
                        .Select(row => peerLabels[(row.EquityIssuerId, row.ListedTicker)])
                        .ToList()
                );
            });
        var ranked = (
            sort == StockHolderTargetSort.PeerWeight
                ? scored
                    .OrderByDescending(target => target.PeerWeight)
                    .ThenByDescending(target => target.EquityAum)
                : scored
                    .OrderByDescending(target => target.PeerValue)
                    .ThenByDescending(target => target.PeerWeight)
        )
            .ThenBy(target => target.HolderId)
            .ToList();

        var shownIds = ranked.Take(500).Select(target => target.HolderId).ToList();
        var holders = await _holderRepository
            .GetAll()
            .AsNoTracking()
            .Where(holder => shownIds.Contains(holder.Id))
            .Select(holder => new
            {
                holder.Id,
                holder.Cik,
                holder.Name,
            })
            .ToDictionaryAsync(holder => holder.Id, cancellationToken);
        var types = await _holderRepository.GetEntityTypes(shownIds, cancellationToken);
        return new StockHolderTargets
        {
            ReportDate = reportDate,
            Sort = sort,
            SectorFallback = sectorFallback,
            SectorName = sectorName,
            Peers = peers,
            TotalTargets = ranked.Count,
            Targets = ranked
                .Take(500)
                .Where(target => holders.ContainsKey(target.HolderId))
                .Select(target => new StockHolderTarget
                {
                    HolderId = target.HolderId,
                    Cik = holders[target.HolderId].Cik,
                    Name = holders[target.HolderId].Name,
                    EquityAum = target.EquityAum,
                    PeerValue = target.PeerValue,
                    PeerWeight = target.PeerWeight,
                    PeersHeld = target.PeersHeld,
                    TopPeers = target.TopPeers,
                    StyleBand = target.StyleBand,
                    EntityType = types.TryGetValue(target.HolderId, out var type) ? type : null,
                })
                .ToList(),
        };
    }
}
