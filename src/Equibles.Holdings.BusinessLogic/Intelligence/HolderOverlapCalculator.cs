using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Each company's closest holder-overlap peers: the companies that share the most active filers'
/// hundred largest positions with it, by cosine similarity of the two holder sets. Only filers
/// measured concentrated or diversified active count, since an index-like or systematic book holds
/// everything together and would make every large stock every other's peer.
/// </summary>
/// <remarks>
/// Peers are operating companies, measured per issuer: a fund (<see cref="FundSecurities"/>) never
/// counts, and a filer's positions in an issuer's share classes add up to one position, so one
/// company appears once (rows carry no listed ticker). A filer keeps only its hundred largest
/// companies as indexes; peers are then counted one company at a time over an inverted index, so
/// memory never holds a pair table.
/// </remarks>
public sealed class HolderOverlapCalculator
{
    /// <summary>The positions of each filer that count, largest value first.</summary>
    public const int PositionsPerHolder = 100;

    /// <summary>Peers must share at least this many filers.</summary>
    public const int MinimumSharedHolders = 3;

    /// <summary>Peers kept per company.</summary>
    public const int PeersPerSecurity = 20;

    private readonly FundSecurities _funds;
    private readonly Dictionary<Guid, int> _indexes = new();
    private readonly List<Guid> _issuers = [];
    private readonly List<int[]> _holders = [];

    public HolderOverlapCalculator(FundSecurities funds = null)
    {
        _funds = funds ?? FundSecurities.None;
    }

    /// <summary>Whether a filer's book counts toward the overlap.</summary>
    public static bool Counts(HolderStyleBand band) =>
        band is HolderStyleBand.Concentrated or HolderStyleBand.DiversifiedActive;

    /// <summary>
    /// Adds one active filer's book; only its largest valued operating companies are kept, each
    /// issuer's classes summed, and a company with any unvalued class is left out.
    /// </summary>
    public void Add(IReadOnlyList<HolderEquityPosition> book)
    {
        var top = book.Where(position => position.Shares > 0 && !_funds.IsFund(position.Security))
            .GroupBy(position => position.Security.IssuerId)
            .Where(group => group.All(position => position.Value != null))
            .Select(group =>
                (Issuer: group.Key, Value: group.Sum(position => position.Value.GetValueOrDefault()))
            )
            .Where(position => position.Value > 0)
            .OrderByDescending(position => position.Value)
            .ThenBy(position => position.Issuer)
            .Take(PositionsPerHolder)
            .Select(position => Index(position.Issuer))
            .ToArray();
        if (top.Length > 1)
            _holders.Add(top);
    }

    /// <summary>The peers of every company held by enough filers, ranked from 1.</summary>
    public List<StockQuarterlyHolderOverlap> Build(DateOnly reportDate)
    {
        var holdersOf = new List<int>[_issuers.Count];
        for (var company = 0; company < holdersOf.Length; company++)
            holdersOf[company] = [];
        for (var holder = 0; holder < _holders.Count; holder++)
            foreach (var company in _holders[holder])
                holdersOf[company].Add(holder);

        var rows = new List<StockQuarterlyHolderOverlap>();
        var shared = new int[_issuers.Count];
        var touched = new List<int>();
        for (var company = 0; company < holdersOf.Length; company++)
        {
            var own = holdersOf[company].Count;
            if (own < MinimumSharedHolders)
                continue;
            foreach (var holder in holdersOf[company])
            foreach (var peer in _holders[holder])
                if (peer != company && shared[peer]++ == 0)
                    touched.Add(peer);

            var issuer = _issuers[company];
            var peers = touched
                .Where(peer => shared[peer] >= MinimumSharedHolders)
                .Select(peer =>
                    (
                        Peer: peer,
                        Shared: shared[peer],
                        Similarity: shared[peer] / Math.Sqrt((double)own * holdersOf[peer].Count)
                    )
                )
                .OrderByDescending(peer => peer.Similarity)
                .ThenByDescending(peer => peer.Shared)
                .ThenBy(peer => _issuers[peer.Peer])
                .Take(PeersPerSecurity)
                .ToList();
            for (var rank = 0; rank < peers.Count; rank++)
            {
                rows.Add(
                    new StockQuarterlyHolderOverlap
                    {
                        EquityIssuerId = issuer,
                        ReportDate = reportDate,
                        Rank = rank + 1,
                        PeerEquityIssuerId = _issuers[peers[rank].Peer],
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

    private int Index(Guid issuer)
    {
        if (!_indexes.TryGetValue(issuer, out var index))
        {
            index = _issuers.Count;
            _indexes[issuer] = index;
            _issuers.Add(issuer);
        }
        return index;
    }
}
