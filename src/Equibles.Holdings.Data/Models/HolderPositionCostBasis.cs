using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The estimated average cost per share of one 13F filer's position in one listed security, one
/// slot per quarter as the presence masks are: slot i and bit i are the i-th quarter end since
/// 2020-03-31. Buys are costed at the security's quarter average price, sells leave the cost per
/// share unchanged, and a split restates the carried cost onto the next quarter's share basis. A
/// zero slot has no estimate: the position was not held, a buy could not be costed
/// (<see cref="UnpricedMask"/>), or it was already held when the filer's comparable history starts.
/// </summary>
/// <remarks>
/// One row per position for its whole history keeps the table below the size of
/// <see cref="HolderStockPresence"/>; a row per quarter would hold every 13F position ever filed
/// again. A position that was never costed or unpriced has no row. The issuer id carries no
/// foreign key, so an issuer merge moves rows by rule.
/// </remarks>
public class HolderPositionCostBasis
{
    // Client-generated, so FlexLabs UpsertRange sends it; a store-generated key inserts null.
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }
    public Guid EquityIssuerId { get; set; }

    /// <summary>Null for the issuer's primary listing, as on the holding row.</summary>
    [MaxLength(32)]
    public string ListedTicker { get; set; }

    /// <summary>
    /// The estimated cost per share at each quarter end, on that quarter's filing share basis;
    /// zero where there is no estimate. Stored single-precision: it is an estimate.
    /// </summary>
    [Required]
    public float[] CostPerShare { get; set; } = [];

    /// <summary>
    /// Quarters a buy since the position was opened could not be costed (no trusted quarter
    /// average price, an unresolved split basis, or shares at odds with the reported value); the
    /// cost stays unavailable until the position is fully sold.
    /// </summary>
    public long UnpricedMask { get; set; }
}
