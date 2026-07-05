using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data.Models;

[Index(nameof(CommonStockId), nameof(TransactionDate))]
[Index(nameof(InsiderOwnerId), nameof(TransactionDate))]
[Index(nameof(AccessionNumber), nameof(TransactionOrder), IsUnique = true)]
// The scraper's known-accession prefilter probes SupersededAccessionNumber with the same
// candidate list as AccessionNumber; without its own index that arm falls back to a
// sequential scan of the table on every sweep batch.
[Index(nameof(SupersededAccessionNumber))]
[Index(nameof(FilingDate))]
[Index(nameof(TransactionDate))]
[Index(nameof(IsPriceValid), nameof(TransactionDate))]
[Index(nameof(SecurityKind), nameof(TransactionDate))]
[Index(nameof(ParserVersion))]
public class InsiderTransaction
{
    /// <summary>
    /// Version of the parsing algorithm that produced this row. Bumped whenever
    /// the parser starts extracting something new from the filing XML (a new
    /// field, a corrected classification). Rows below this value can be
    /// re-parsed from the cached <see cref="InsiderFiling"/> XML rather than
    /// re-fetched from EDGAR. Rows ingested before versioning default to 0.
    ///
    /// History: v1 added SecurityKind; v2 added <see cref="Notes"/> (footnotes);
    /// v3 restates per-ADS prices on ADS/ADR rows to per-ordinary so Shares ×
    /// PricePerShare is a real value (re-evaluated from the footnotes — see
    /// <see cref="ReportedPricePerShare"/>); v4 captures the Rule 10b5-1
    /// affirmative-defense checkbox into <see cref="IsRule10b5One"/>; v5 tags
    /// holding-only rows (parsed from a <c>nonDerivativeHolding</c>/
    /// <c>derivativeHolding</c> element) as <see cref="TransactionCode.Holding"/>
    /// instead of <see cref="TransactionCode.Other"/>, so the reprocess re-tags
    /// every historical holding row and transaction lists can exclude them.
    /// </summary>
    public const int CurrentParserVersion = 5;

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InsiderOwnerId { get; set; }
    public virtual InsiderOwner InsiderOwner { get; set; }

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly FilingDate { get; set; }
    public DateOnly TransactionDate { get; set; }

    public TransactionCode TransactionCode { get; set; }

    public long Shares { get; set; }

    /// <summary>
    /// Effective per-share price used by dashboards and sorts. Equals
    /// <see cref="ReportedPricePerShare"/> for normal rows; diverges only
    /// when a fat-fingered filing was repaired — see <see cref="IsPriceValid"/>.
    /// </summary>
    public decimal PricePerShare { get; set; }

    /// <summary>
    /// The per-share price exactly as filed, before any repair. Always
    /// populated, so the raw filing value is never lost. When a filer types
    /// the total transaction value into the per-share field, the repair
    /// recovers the unit price as <c>ReportedPricePerShare / Shares</c> and
    /// stores it in <see cref="PricePerShare"/>, leaving this column holding
    /// the original (bad) value for audit and reversibility.
    /// </summary>
    public decimal ReportedPricePerShare { get; set; }

    public AcquiredDisposed AcquiredDisposed { get; set; }
    public long SharesOwnedAfter { get; set; }
    public OwnershipNature OwnershipNature { get; set; }

    [MaxLength(128)]
    public string SecurityTitle { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>
    /// Ordinal position of this row within its Form 3/4 filing (0-based). Form 4 XML
    /// has no per-transaction identifier — uniqueness is by (AccessionNumber, position).
    /// </summary>
    public int TransactionOrder { get; set; }

    public bool IsAmendment { get; set; }

    /// <summary>
    /// For rows from a Form 4/A or 3/A: the filing date of the ORIGINAL report the
    /// amendment restates (the XML's <c>dateOfOriginalSubmission</c>). Drives
    /// supersession — an amendment replaces the original filing's rows, and a
    /// late-arriving original (or an older chained amendment) is skipped when a row
    /// referencing its filing date already exists. Null on non-amendment rows and
    /// on amendments whose XML omits the element.
    /// </summary>
    public DateOnly? OriginalFilingDate { get; set; }

    /// <summary>
    /// For amendment rows: the accession number of the original filing this
    /// amendment replaced — or claimed, when the original arrived after the
    /// amendment (EDGAR lists newest-first). Resolved at ingest rather than
    /// parsed: the XML only names the original's submission DATE, which can trail
    /// the feed's filing date (an after-17:30 submission indexes the next
    /// business day), so the accession is the durable link. Also feeds the
    /// scraper's known-accession prefilter, so a superseded original stops being
    /// re-fetched from EDGAR every cycle. Null until resolved.
    /// </summary>
    [MaxLength(32)]
    public string SupersededAccessionNumber { get; set; }

    /// <summary>
    /// Whether this row concerns the issuer's actual shares or a derivative
    /// instrument, taken from the Form 4 table it was parsed from (not the
    /// title text). <see cref="InsiderSecurityKind.Unknown"/> for rows ingested
    /// before this was captured; those are reclassified by a reprocess pass.
    /// </summary>
    public InsiderSecurityKind SecurityKind { get; set; } = InsiderSecurityKind.Unknown;

    /// <summary>
    /// Parsing-algorithm version that produced this row. See
    /// <see cref="CurrentParserVersion"/>. Defaults to 0 for rows ingested
    /// before versioning, which marks them for reprocessing.
    /// </summary>
    public int ParserVersion { get; set; }

    /// <summary>
    /// Footnotes attached to this transaction in the Form 4 filing, resolved to
    /// their text. A Form 4 references footnotes by id (<c>footnoteId</c>) on the
    /// transaction itself or on individual fields; this collects every footnote
    /// referenced anywhere within the row, de-duplicated, in document order.
    /// Empty when the filing annotated nothing. Stored as a Postgres array.
    /// </summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>
    /// Whether this transaction was made pursuant to a Rule 10b5-1(c) trading
    /// plan, from the affirmative-defense checkbox the SEC added to Form 4/5 in
    /// 2023 (the <c>aff10b5One</c> element). The checkbox is a single
    /// document-level flag — one Form 4 can report several transactions, and the
    /// box asserts the affirmative defense for the filing as a whole — so every
    /// row parsed from the same filing carries the same value; the per-trade
    /// detail lives in the footnotes, which are not machine-classified.
    /// <list type="bullet">
    /// <item><c>true</c> — the box is checked: a pre-planned, automatic trade.</item>
    /// <item><c>false</c> — the box is present and unchecked.</item>
    /// <item><c>null</c> — the filing predates the checkbox (schema older than
    /// EDGAR 23.1) or otherwise omits the element, so pre-planned status is
    /// unknown rather than negative.</item>
    /// </list>
    /// </summary>
    public bool? IsRule10b5One { get; set; }

    /// <summary>
    /// Tri-state price-plausibility flag, cross-checked against the Yahoo
    /// unadjusted close on TransactionDate:
    /// <list type="bullet">
    /// <item><c>null</c> — not evaluated yet (no close available when the row
    /// was ingested); a later maintenance recompute re-checks it once the
    /// close exists.</item>
    /// <item><c>true</c> — plausible, or repaired (see
    /// <see cref="ReportedPricePerShare"/>).</item>
    /// <item><c>false</c> — implausible and not repairable (<see cref="Shares"/>
    /// is 0, so the mis-entered total can't be divided back to a unit price).</item>
    /// </list>
    /// Dashboards show a row unless this is positively <c>false</c>, so a
    /// single fat-fingered Form 4 doesn't drown out every other row when
    /// sorted by Shares × PricePerShare.
    /// </summary>
    public bool? IsPriceValid { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
