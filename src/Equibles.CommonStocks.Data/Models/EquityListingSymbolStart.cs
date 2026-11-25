using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// The first date a symbol names this exact listing in sources keyed by symbol. A source row
/// dated earlier under that symbol belonged to the security that held the symbol before, so an
/// import that maps today's symbols onto every historical file skips it.
/// </summary>
[PrimaryKey(nameof(EquityListingId), nameof(Ticker))]
public class EquityListingSymbolStart
{
    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    [Required, MaxLength(32)]
    public string Ticker { get; set; }

    public DateOnly StartsOn { get; set; }

    [Required, MaxLength(64)]
    public string EvidenceSource { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
