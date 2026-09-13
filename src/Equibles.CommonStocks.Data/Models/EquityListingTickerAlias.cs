using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// A recorded symbol of this exact listing; never an issuer-wide redirect or inferred trading period.
[PrimaryKey(nameof(EquityListingId), nameof(Ticker))]
public class EquityListingTickerAlias
{
    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    [Required, MaxLength(32)]
    public string Ticker { get; set; }

    [Required, MaxLength(64)]
    public string EvidenceSource { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
