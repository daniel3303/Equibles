using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.Data.Models;

[Index(nameof(EquityListingId), nameof(WeekStartDate), IsUnique = true)]
[Index(nameof(WeekStartDate))]
public class OffExchangeVolume
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    // Preserve original source attribution independently of the stable listing identity.
    [Required]
    [MaxLength(TickerNormalizer.MaxListedLength)]
    public string ListedTicker { get; set; }

    public DateOnly WeekStartDate { get; set; }

    public long AtsVolume { get; set; }
    public long AtsTradeCount { get; set; }
    public long NonAtsOtcVolume { get; set; }
    public long NonAtsOtcTradeCount { get; set; }

    // The off-exchange share of consolidated volume is computed at read time:
    // the FINRA OTC/ATS Transparency file does not include consolidated tape
    // volume, so that ratio is not stored on this record.

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
