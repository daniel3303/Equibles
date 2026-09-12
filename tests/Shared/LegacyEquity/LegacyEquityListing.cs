using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// Immutable translation of the old composite identity. No FK to CommonStock: history
// must retain its source identity even after an old writer deletes the legacy owner.
[PrimaryKey(nameof(CommonStockId), nameof(ListedTicker))]
[Index(nameof(EquityListingId), IsUnique = true)]
public class LegacyEquityListing
{
    public Guid CommonStockId { get; set; }

    [MaxLength(32)]
    public string ListedTicker { get; set; }
    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }
}
