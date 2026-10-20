namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>Whether a held position's cost per share could be estimated, and why not.</summary>
public enum HolderCostStatus
{
    /// <summary>Every buy since the position opened was costed at a quarter average price.</summary>
    Estimated,

    /// <summary>
    /// The position was already held when the filer's comparable 13F history starts (the corpus
    /// start, its first filing or a gap in its filings), so the entry price is unknown.
    /// </summary>
    UnknownEntry,

    /// <summary>
    /// A buy since the position opened had no trusted quarter average price, crossed an unresolved
    /// split, or its shares disagreed with the reported value; unavailable until fully sold.
    /// </summary>
    Unpriced,
}
