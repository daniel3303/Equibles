namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>How shareholder targets are ranked.</summary>
public enum StockHolderTargetSort
{
    /// <summary>Money in the peers: peer weight times book size.</summary>
    PeerValue,

    /// <summary>The peers' share of the filer's book, then book size.</summary>
    PeerWeight,
}
