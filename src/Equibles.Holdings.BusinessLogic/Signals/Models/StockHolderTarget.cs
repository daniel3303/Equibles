using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>
/// A 13F filer that holds a stock's peers but not the stock: how much of its book the peers take,
/// its book size, and its measured style and regulatory type.
/// </summary>
public sealed record StockHolderTarget
{
    public Guid HolderId { get; init; }
    public string Cik { get; init; }
    public string Name { get; init; }

    /// <summary>The filer's valued 13F common-share book that quarter.</summary>
    public long EquityAum { get; init; }

    public long PeerValue { get; init; }

    /// <summary>Peer value over the book, from 0 to 1.</summary>
    public decimal PeerWeight { get; init; }

    public int PeersHeld { get; init; }

    /// <summary>The peers it holds most of, largest first.</summary>
    public IReadOnlyList<string> TopPeers { get; init; } = [];

    public HolderStyleBand? StyleBand { get; init; }
    public HolderEntityType? EntityType { get; init; }
}
