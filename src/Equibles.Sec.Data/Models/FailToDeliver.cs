using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

[Index(nameof(EquityListingId), nameof(SettlementDate), IsUnique = true)]
[Index(nameof(SettlementDate))]
public class FailToDeliver
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    // Original source attribution; listing identity does not change when a ticker is renamed.
    [Required]
    [MaxLength(TickerNormalizer.MaxListedLength)]
    public string ListedTicker { get; set; }

    public DateOnly SettlementDate { get; set; }

    public long Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
