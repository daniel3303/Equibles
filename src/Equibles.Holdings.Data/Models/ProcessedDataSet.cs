using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Index(nameof(FileName), IsUnique = true)]
public class ProcessedDataSet
{
    // Sentinel row name. Never matches a real quarterly data-set file, so it
    // is never downloaded/processed, but its presence keeps the table
    // non-empty so HoldingsScraperWorker.BackfillProcessedDataSets does NOT
    // re-seed history as "processed" after StockCusipChangedConsumer clears
    // the real rows for a backfill.
    public const string BackfillGuardFileName = "__backfill-guard__";

    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(128)]
    public string FileName { get; set; }

    public int SubmissionCount { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
