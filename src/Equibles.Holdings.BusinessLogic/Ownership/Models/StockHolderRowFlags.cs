namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>What a reader must know before using a unified holder-list row.</summary>
[Flags]
public enum StockHolderRowFlags
{
    None = 0,

    /// <summary>
    /// A zero balance, an insider filing older than 24 months, a 13D or 13G older than the 13F
    /// quarter of the holder or of the parent it nests under, or a row its own lane dates as no
    /// longer current; hidden from the default view.
    /// </summary>
    Stale = 1 << 0,

    /// <summary>A split after the row's date cannot be attributed, so its shares stay as filed and % outstanding is unavailable.</summary>
    SplitUnresolved = 1 << 1,

    /// <summary>The balance covers every class of the issuer (Forms 3, 4 and 5), not one listing.</summary>
    IssuerWide = 1 << 2,

    /// <summary>The ownership summary does not add this row's shares.</summary>
    NotSummed = 1 << 3,
}
