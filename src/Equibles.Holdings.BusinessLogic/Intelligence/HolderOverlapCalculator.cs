using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Each security's closest holder-overlap peers: the securities that share the most active filers'
/// hundred largest positions with it, by cosine similarity of the two holder sets. Only filers
/// measured concentrated or diversified active count, since an index-like or systematic book holds
/// everything together and would make every large stock every other's peer.
/// </summary>
/// <remarks>
/// A filer keeps only its hundred largest positions as security indexes; peers are then counted
/// one security at a time over an inverted index, so memory never holds a pair table.
/// </remarks>
public sealed class HolderOverlapCalculator
{
    /// <summary>The positions of each filer that count, largest value first.</summary>
    public const int PositionsPerHolder = 100;

    /// <summary>Peers must share at least this many filers.</summary>
    public const int MinimumSharedHolders = 3;

    /// <summary>Peers kept per security.</summary>
    public const int PeersPerSecurity = 20;

    private readonly Dictionary<HolderSecurityKey, int> _indexes = new();
    private readonly List<HolderSecurityKey> _securities = [];
    private readonly List<int[]> _holders = [];

    /// <summary>Whether a filer's book counts toward the overlap.</summary>
    public static bool Counts(HolderStyleBand band) =>
        band is HolderStyleBand.Concentrated or HolderStyleBand.DiversifiedActive;

    /// <summary>Adds one active filer's book; only its largest valued positions are kept.</summary>
    public void Add(IReadOnlyList<HolderEquityPosition> book)
    {
        var top = book.Where(position => position.Shares > 0)
            .GroupBy(position => position.Security)
            .Where(group => group.All(position => position.Value != null))
            .Select(group =>
                (
                    Security: group.Key,
                    Value: group.Sum(position => position.Value.GetValueOrDefault())
                )
            )
            .Where(position => position.Value > 0)
            .OrderByDescending(position => position.Value)
            .ThenBy(position => position.Security.IssuerId)
            .ThenBy(position => position.Security.ListedTicker, StringComparer.Ordinal)
            .Take(PositionsPerHolder)
            .Select(position => Index(position.Security))
            .ToArray();
        if (top.Length > 1)
            _holders.Add(top);
    }

    /// <summary>The peers of every security held by enough filers, ranked from 1.</summary>
    public List<StockQuarterlyHolderOverlap> Build(DateOnly reportDate)
    {
        var holdersOf = new List<int>[_securities.Count];
        for (var security = 0; security < holdersOf.Length; security++)
            holdersOf[security] = [];
        for (var holder = 0; holder < _holders.Count; holder++)
            foreach (var security in _holders[holder])
                holdersOf[security].Add(holder);

        var rows = new List<StockQuarterlyHolderOverlap>();
        var shared = new int[_securities.Count];
        var touched = new List<int>();
        for (var security = 0; security < holdersOf.Length; security++)
        {
            var own = holdersOf[security].Count;
            if (own < MinimumSharedHolders)
                continue;
            foreach (var holder in holdersOf[security])
            foreach (var peer in _holders[holder])
                if (peer != security && shared[peer]++ == 0)
                    touched.Add(peer);

            var key = _securities[security];
            // A sibling class of the same issuer is the same company, never its peer.
            var peers = touched
                .Where(peer =>
                    shared[peer] >= MinimumSharedHolders
                    && _securities[peer].IssuerId != key.IssuerId
                )
                .Select(peer =>
                    (
                        Peer: peer,
                        Shared: shared[peer],
                        Similarity: shared[peer] / Math.Sqrt((double)own * holdersOf[peer].Count)
                    )
                )
                .OrderByDescending(peer => peer.Similarity)
                .ThenByDescending(peer => peer.Shared)
                .ThenBy(peer => _securities[peer.Peer].IssuerId)
                .ThenBy(peer => _securities[peer.Peer].ListedTicker, StringComparer.Ordinal)
                .Take(PeersPerSecurity)
                .ToList();
            for (var rank = 0; rank < peers.Count; rank++)
            {
                var peerKey = _securities[peers[rank].Peer];
                rows.Add(
                    new StockQuarterlyHolderOverlap
                    {
                        EquityIssuerId = key.IssuerId,
                        ListedTicker = key.ListedTicker,
                        ReportDate = reportDate,
                        Rank = rank + 1,
                        PeerEquityIssuerId = peerKey.IssuerId,
                        PeerListedTicker = peerKey.ListedTicker,
                        SharedHolders = peers[rank].Shared,
                        Similarity = Math.Round((decimal)peers[rank].Similarity, 6),
                    }
                );
            }
            foreach (var peer in touched)
                shared[peer] = 0;
            touched.Clear();
        }
        return rows;
    }

    private int Index(HolderSecurityKey security)
    {
        if (!_indexes.TryGetValue(security, out var index))
        {
            index = _securities.Count;
            _indexes[security] = index;
            _securities.Add(security);
        }
        return index;
    }
}
