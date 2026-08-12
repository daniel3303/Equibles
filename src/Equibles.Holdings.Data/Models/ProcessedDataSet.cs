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

    // Sentinel row name marking a CUSIP-identity rescan as QUEUED but not yet
    // applied. StockCusipChangedConsumer only adds this row; the actual ledger
    // clear happens at the start of HoldingsScraperWorker's next cycle. Clearing
    // inline restarted the multi-hour oldest-first walk on every identity
    // discovery, and the FTD sweeps discover identities near-daily — so the walk
    // never reached the newest quarters and they never healed
    // (EquiblesCommercial#7163). Deferring the clear lets every walk complete;
    // events arriving mid-walk coalesce into one queued rescan for the next one.
    public const string RescanPendingFileName = "__rescan-pending__";

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
    /// Version 7: resolve sibling-listing CUSIPs to their exact listed ticker
    /// (#4247). CUSIPs of a filer's OTHER listed securities (Alphabet Class C at
    /// 02079K107 beside GOOGL's 02079K305) matched nothing, so every such 13F
    /// line was dropped at import — Alphabet's institutional ownership was
    /// missing its entire Class C side. The re-import maps them through
    /// CommonStockListedCusip, keys the rows by (…, ListedTicker), and values
    /// them from the class's own exact price series.
    /// Version 8: keep the derivation when the filed value is on a thousands
    /// basis. Filers still reporting the VALUE column in thousands after the
    /// SEC's 2023 whole-dollar switch made the correct derivation look 1,000×
    /// "too big", so the sanity guard published the thousands-scale filed
    /// figure and served their books 1,000× understated (Baupost's ~$5B book
    /// read ~$5M). The re-import re-derives those rows under the banded guard
    /// and heals the mis-published history. Open-quarter latency, accepted:
    /// realtime-swept accessions are behind the realtime watermark, not this
    /// ledger, so the current quarter's affected rows stay understated until
    /// its bulk quarterly data set lands and re-imports them.
    /// Version 9: stop deduplicating away an original superseded only by a
    /// "NEW HOLDINGS" amendment (EquiblesCommercial#7163). That amendment type
    /// only ADDS positions, but the dedup kept just the latest submission per
    /// (CIK, period) — so a filer whose newest filing was a NEW HOLDINGS
    /// amendment had its entire original book skipped on every bulk import
    /// (48 filers in the 2026 Q2 data set alone, including all three
    /// restructured Vanguard entities, whose Q1 2026 XOM/BRK-B positions could
    /// never heal). The re-import walks history with the original + additive
    /// amendments both retained.
    /// </summary>
    public const int CurrentParserVersion = 9;

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
