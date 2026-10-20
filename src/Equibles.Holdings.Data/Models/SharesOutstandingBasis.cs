using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>Where a quarter's shares-outstanding count came from, and so what it can divide.</summary>
public enum SharesOutstandingBasis
{
    /// <summary>No cover-page count dated near enough to the quarter end.</summary>
    [Display(Name = "None")]
    None = 0,

    /// <summary>One consolidated cover-page count for the whole common stock.</summary>
    [Display(Name = "Consolidated cover page")]
    Consolidated = 1,

    /// <summary>
    /// The sum of per-class cover-page counts: the entity total, which no one listing's holders
    /// can be divided by.
    /// </summary>
    [Display(Name = "Sum of share classes")]
    ClassSum = 2,

    /// <summary>
    /// A foreign private issuer's count in ordinary shares, a different unit from a US depositary
    /// listing.
    /// </summary>
    [Display(Name = "Foreign ordinary shares")]
    ForeignOrdinary = 3,
}
