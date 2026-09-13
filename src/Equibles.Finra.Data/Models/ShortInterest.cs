using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.Data.Models;

[Index(nameof(EquityListingId), nameof(SettlementDate), IsUnique = true)]
[Index(nameof(SettlementDate))]
public class ShortInterest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    // Preserve original source attribution independently of the stable listing identity.
    [Required]
    [MaxLength(TickerNormalizer.MaxListedLength)]
    public string ListedTicker { get; set; }

    public DateOnly SettlementDate { get; set; }

    public long CurrentShortPosition { get; set; }
    public long PreviousShortPosition { get; set; }
    public long ChangeInShortPosition { get; set; }

    public long? AverageDailyVolume { get; set; }
    public decimal? DaysToCover { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
