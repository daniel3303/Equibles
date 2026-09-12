using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// One authoritative inactive common-stock listing belonging to an SEC filer. A filer may have
/// several exact listed securities, so each symbol keeps its own final trading date and historical
/// backfill state rather than inheriting an arbitrary filer-level cutoff.
/// </summary>
[Index(nameof(EquityIssuerId), nameof(ListedTicker), IsUnique = true)]
[Index(nameof(ListedTicker), nameof(DelistedOn))]
[Table("CommonStockDelistedListing")]
public class EquityListingRetirementEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("CommonStockId")]
    public Guid EquityIssuerId { get; set; }

    public virtual EquityIssuer Issuer { get; set; }

    [Required]
    [MaxLength(32)]
    public string ListedTicker { get; set; }

    public DateOnly DelistedOn { get; set; }

    public DateTime? HistoricalPriceBackfillAttemptedAt { get; set; }

    [MaxLength(9)]
    public string Cusip { get; set; }

    public DateTime? HistoricalCusipBackfillRequestedAt { get; set; }

    public List<string> HistoricalCusipBackfillCandidates { get; set; } = [];

    public DateOnly? HistoricalCusipBackfillCandidateOn { get; set; }

    public bool HistoricalCusipBackfillAmbiguous { get; set; }

    public DateTime? HistoricalCusipBackfillSweepStartedAt { get; set; }
}
