using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// An upcoming (or recently past) earnings release date for a company's fiscal
/// quarter, scraped from its IR earnings calendar. One row per fiscal period:
/// the unique (CommonStockId, FiscalYear, FiscalQuarter) index lets a later scrape
/// update a previously-estimated date to a confirmed one in place rather than
/// inserting a duplicate.
/// </summary>
[Index(nameof(CommonStockId), nameof(FiscalYear), nameof(FiscalQuarter), IsUnique = true)]
[Index(nameof(ScheduledDate))]
public class EarningsCalendarEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public int FiscalYear { get; set; }

    /// <summary>Fiscal quarter, 1-4.</summary>
    public int FiscalQuarter { get; set; }

    /// <summary>The release date as scheduled by the company.</summary>
    public DateOnly ScheduledDate { get; set; }

    /// <summary>
    /// True when the company has confirmed the date; false when it is still the
    /// platform's estimate. A later scrape can flip this without changing identity.
    /// </summary>
    public bool IsConfirmed { get; set; }

    /// <summary>Source page URL when the platform provides one; otherwise null.</summary>
    [MaxLength(1024)]
    public string Url { get; set; }

    /// <summary>The IR platform this entry was scraped from.</summary>
    public IrPlatformType Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
