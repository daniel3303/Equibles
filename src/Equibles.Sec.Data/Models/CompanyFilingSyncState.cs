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
/// re-sweep that backstops the real-time discovery feeds. The checkpoint belongs
/// to the issuer and survives retirement of a listing or the legacy stock row.
/// </summary>
[Index(nameof(LastSyncedAt))]
public class CompanyFilingSyncState
{
    [Key]
    // Retain the deployed column name until every older binary has retired.
    public Guid EquityIssuerId { get; set; }

    [ForeignKey(nameof(EquityIssuerId))]
    public virtual EquityIssuer Issuer { get; set; }

    public DateTime LastSyncedAt { get; set; }
}
