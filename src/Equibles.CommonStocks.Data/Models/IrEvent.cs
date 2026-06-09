using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// A scheduled investor-relations event (earnings webcast, conference appearance,
/// presentation, shareholder meeting) for a company, scraped from its IR website.
/// The unique (CommonStockId, StartDateTime, Title) index makes re-scraping
/// idempotent without depending on a stable event URL, which several platforms omit.
/// </summary>
[Index(nameof(CommonStockId), nameof(StartDateTime), nameof(Title), IsUnique = true)]
[Index(nameof(CommonStockId), nameof(StartDateTime))]
public class IrEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [MaxLength(512)]
    public string Title { get; set; }

    /// <summary>When the event starts, in UTC.</summary>
    public DateTime StartDateTime { get; set; }

    public IrEventType Type { get; set; }

    /// <summary>Registration / webcast URL when the source provides one; otherwise null.</summary>
    [MaxLength(1024)]
    public string Url { get; set; }

    /// <summary>The IR platform this event was scraped from.</summary>
    public IrPlatformType Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
