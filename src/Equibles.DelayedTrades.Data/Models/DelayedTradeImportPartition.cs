using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.DelayedTrades.Data.Models;

// One settled session of one market written to the price store; the marker is what makes the settle pass idempotent.
[PrimaryKey(nameof(Dataset), nameof(PartitionDate), nameof(ScopeKey))]
[Index(nameof(Dataset), nameof(ScopeKey), nameof(PartitionDate))]
public class DelayedTradeImportPartition
{
    [MaxLength(64)]
    public string Dataset { get; set; }

    public DateOnly PartitionDate { get; set; }

    [MaxLength(80)]
    public string ScopeKey { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public int RederivedCount { get; set; }

    [Required, MaxLength(500)]
    public string SourceUrl { get; set; }

    [Required, MaxLength(500)]
    public string TermsUrl { get; set; }

    [MaxLength(64)]
    public string FileSha256 { get; set; }

    public long FileBytes { get; set; }

    public int PrintCount { get; set; }
    public int LitPrintCount { get; set; }
    public int DarkPrintCount { get; set; }
    public int CancelledCount { get; set; }
    public int AmendedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int OutOfSessionCount { get; set; }
    public int IsinCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int CurrencyMismatchCount { get; set; }

    public int BarsInserted { get; set; }
    public int BarsOverwroteYahoo { get; set; }
    public int BarsRederived { get; set; }
    public int BarsSkippedBasis { get; set; }
    public int BarsSkippedInvalid { get; set; }
    public int BarsSkippedIdentity { get; set; }
    public int BarsUnsettled { get; set; }
}
