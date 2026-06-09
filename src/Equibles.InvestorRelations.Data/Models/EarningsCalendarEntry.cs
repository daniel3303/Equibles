using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InvestorRelations.Data.Models;

// A forward-looking earnings date scraped from a company's investor relations page.
// Distinct from filed transcripts (those are retrospective): one entry per company per
// scheduled report date, so (CommonStockId, ScheduledDate) is the natural key.
[Index(nameof(CommonStockId), nameof(ScheduledDate), IsUnique = true)]
[Index(nameof(ScheduledDate))]
public class EarningsCalendarEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly ScheduledDate { get; set; }

    public EarningsFiscalPeriod FiscalPeriod { get; set; }

    public int? FiscalYear { get; set; }

    public EarningsCallTimeOfDay TimeOfDay { get; set; }

    // True when the company has confirmed the date; false when it is an estimate.
    public bool IsConfirmed { get; set; }

    [MaxLength(512)]
    public string Url { get; set; }

    public IrPlatformType Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
