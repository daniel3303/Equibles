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
    /// Version 3: restate as-filed share counts onto the price series' basis
    /// before deriving Value (#4242). Every position on a stock that split after
    /// its report date was multiplied across two share bases and is wrong by the
    /// split ratio — 1.87M rows carrying $39.3T that should read $90.2T, mostly
    /// understated by forward splits and a smaller set inflated up to 200x by
    /// reverse ones. Nothing recomputes a stored value, so only a re-import
    /// heals them.
    /// Version 4: parse the summary page's declared totals (tableEntryTotal /
    /// tableValueTotal) onto the filing rollup (#4251) so surfaces can say "we
    /// track 7 of the 8 positions this filing declares"; the same re-import also
    /// rebuilds the unmapped-CUSIP queue through the per-key flush (#4249),
    /// healing the under-counts the old slice-wipe left behind.
    /// Version 5: keep the identifiers filed alongside every other-manager name
    /// (#4263). Both of a 13F's other-manager lists carry a CIK, a Form 13F file
    /// number and a CRD, and the import read only the name — so a combination
    /// report's subsidiaries were stored as unmatched strings and the
    /// parent/subsidiary structure they describe could not be recovered. The
    /// re-import populates FilingOtherManager for all history.
    /// Version 6: read OTHERMANAGER as the comma-separated LIST it is (#4264).
    /// A plain int parse rejects "4,8,11", so every multi-manager attribution
    /// became "no manager" — Berkshire's filings lost ~85% of their manager
    /// split this way. The re-import credits each leg to the first referenced
    /// manager and keeps the raw list on SharedManagerNumbers, healing the
    /// sets version 5 imported with the nulled legs.
    /// </summary>
    public const int CurrentParserVersion = 6;

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
