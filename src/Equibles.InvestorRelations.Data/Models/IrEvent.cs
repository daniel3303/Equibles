using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InvestorRelations.Data.Models;

// An investor relations event — earnings call, conference appearance, webcast, annual
// meeting, etc. (CommonStockId, Title, ScheduledDate) is the natural key so re-scraping
// the same listing does not duplicate it.
[Index(nameof(CommonStockId), nameof(Title), nameof(ScheduledDate), IsUnique = true)]
[Index(nameof(ScheduledDate))]
public class IrEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [Required]
    [MaxLength(512)]
    public string Title { get; set; }

    public IrEventType EventType { get; set; }

    public DateTime ScheduledDate { get; set; }

    // Webcast / registration link when published.
    [MaxLength(512)]
    public string Url { get; set; }

    [MaxLength(256)]
    public string Location { get; set; }

    public IrPlatformType Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
