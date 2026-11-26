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

    // The earlier holder's run of source rows the start rests on, kept because a withdrawal
    // removes those rows and the start must outlive them.
    public DateOnly? EarlierHolderFrom { get; set; }

    public DateOnly? EarlierHolderThrough { get; set; }

    // The first date whose rows under the symbol were withdrawn as the earlier holder's; set
    // before they go, so a start lifted later can have that span read again.
    public DateOnly? WithdrawnFrom { get; set; }

    [Required, MaxLength(64)]
    public string EvidenceSource { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
