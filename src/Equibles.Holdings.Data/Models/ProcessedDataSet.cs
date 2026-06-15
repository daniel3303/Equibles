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

    /// <summary>
    /// The 13F import pipeline's current parser version. Bump this when a
    /// parser fix must re-apply to already-imported data: the scraper treats
    /// any data set processed at a lower version as unprocessed and re-imports
    /// it on the next cycle (oldest first, so amendments re-apply after their
    /// originals). Mirrors <c>NportFiling.CurrentParserVersion</c>.
    /// Version 1: duplicated share-count column repair (#3499).
    /// Version 2: scope restatement-amendment deletes to the amendment's own
    /// filing type (#3738) — re-import all 13F history so a Schedule 13D/G
    /// amendment that previously wiped a same-quarter 13F-HR portfolio is healed.
    /// </summary>
    public const int CurrentParserVersion = 2;

    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(128)]
    public string FileName { get; set; }

    public int SubmissionCount { get; set; }

    /// <summary>
    /// Parser version the pipeline was at when this data set was imported.
    /// Defaults to 0 for rows written before versioning so the first deploy
    /// re-enrolls all history through the current parser.
    /// </summary>
    public int ParserVersion { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
