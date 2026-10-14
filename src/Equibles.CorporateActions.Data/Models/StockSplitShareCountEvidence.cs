namespace Equibles.CorporateActions.Data.Models;

/// <summary>What the issuer's own share counts say about a captured split ratio.</summary>
public enum StockSplitShareCountEvidence
{
    /// <summary>The counts moved by this ratio across the event, or an authoritative source stated it.</summary>
    Confirmed,

    /// <summary>
    /// The counts did not move by this ratio: a price factor such as a spin-off or a depositary
    /// ratio change, which moves prices but not the issuer's share count.
    /// </summary>
    Contradicted,
}
