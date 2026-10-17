using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The quarters a 13F filer held one listed security as a bitmask: bit i is the i-th quarter end
/// since 2020-03-31. Tenure reads the run of set bits; the issuer id carries no foreign key, so an
/// issuer merge leaves rows the next rebuild of each quarter clears.
/// </summary>
public class HolderStockPresence
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

    public long HeldMask { get; set; }
}
