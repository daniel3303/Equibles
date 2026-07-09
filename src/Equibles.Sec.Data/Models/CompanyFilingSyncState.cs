using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// Per-company filing-enumeration watermark for event-driven EDGAR discovery:
/// when the document scraper last listed this company's submissions. No row
/// means the company was never fully synced (fresh onboarding) and gets a full
/// historical backfill; a stale row schedules the periodic reconciliation
/// re-sweep that backstops the real-time discovery feeds. Rows cascade with
/// the stock, so a replaced company re-onboards from scratch.
/// </summary>
[Index(nameof(LastSyncedAt))]
public class CompanyFilingSyncState
{
    [Key]
    public Guid CommonStockId { get; set; }

    [ForeignKey(nameof(CommonStockId))]
    public virtual CommonStock CommonStock { get; set; }

    public DateTime LastSyncedAt { get; set; }
}
