using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// One financial statement reconstructed as-reported from a filing — the issuer's own line
/// items, labels, indentation, subtotals and comparative period columns. Built by parsing
/// SEC's pre-rendered R-files (the FilingSummary.xml report index + the per-statement
/// <c>R#.htm</c> tables it lists), which SEC renders from the filing's own
/// presentation/calculation/label linkbases. Distinct from <see cref="FinancialFact"/>: a
/// <see cref="FinancialFact"/> is one normalized concept value for cross-company screening,
/// whereas this is a whole statement laid out exactly as the company filed it.
///
/// <para>
/// The line-item tree and its period columns live in <see cref="Payload"/> (JSON) rather than
/// as relational rows: a statement is always read whole, the shape is presentation-oriented
/// (depth, totals, per-column values), and a column-per-row model would explode into millions
/// of tiny rows for no query benefit. Restatements are retained as separate rows discriminated
/// by their source <see cref="Document"/>; the currently-reported statement for a period is the
/// one from the latest-filed document.
/// </para>
/// </summary>
[Index(nameof(CommonStockId), nameof(Kind), nameof(FiscalYear), nameof(FiscalPeriod))]
[Index(nameof(DocumentId))]
[Index(nameof(DocumentId), nameof(RoleUri), IsUnique = true)]
public class ReportedFinancialStatement
{
    // Client-generated Guid key, matching FinancialFact: without
    // DatabaseGeneratedOption.None EF marks it store-generated and the upsert
    // omits it from the INSERT, which Postgres (no column default) rejects.
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    /// <summary>The filing this statement was reconstructed from.</summary>
    public Guid DocumentId { get; set; }
    public virtual Document Document { get; set; }

    /// <summary>Accession number of the source filing, denormalized for filtering.</summary>
    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>Which statement this is, classified from the role title (drives the type tabs).</summary>
    public ReportedStatementKind Kind { get; set; }

    /// <summary>
    /// The issuer's role URI for the statement (FilingSummary <c>Role</c>), e.g.
    /// <c>http://www.acme.com/role/ConsolidatedStatementsOfOperations</c>. Stable within a
    /// filing, so it forms the upsert natural key together with <see cref="DocumentId"/>.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string RoleUri { get; set; }

    /// <summary>The human title SEC renders for the statement (FilingSummary <c>ShortName</c>).</summary>
    [MaxLength(512)]
    public string RoleShortName { get; set; }

    /// <summary>The R-file the statement was parsed from, e.g. <c>R2.htm</c> (traceability / re-parse).</summary>
    [MaxLength(64)]
    public string ReportFileName { get; set; }

    /// <summary>
    /// A parenthetical companion statement (e.g. "Balance Sheet (Parenthetical)" carrying
    /// per-share and shares-authorized detail) rather than the primary statement.
    /// </summary>
    public bool IsParenthetical { get; set; }

    public int FiscalYear { get; set; }

    public SecFiscalPeriod FiscalPeriod { get; set; }

    /// <summary>Period end of the statement's primary (newest) column — the period it represents.</summary>
    public DateOnly PrimaryPeriodEnd { get; set; }

    /// <summary>Source form (10-K, 10-Q, 20-F, …). Reuses the SEC module's DocumentType.</summary>
    [Required]
    public DocumentType Form { get; set; }

    public DateOnly FiledDate { get; set; }

    /// <summary>The report's position within the filing (FilingSummary order), preserved for display ordering.</summary>
    public int Position { get; set; }

    /// <summary>Reporting currency of the statement, e.g. <c>USD</c>; null when not stated.</summary>
    [MaxLength(16)]
    public string Currency { get; set; }

    /// <summary>
    /// The "$ in Thousands/Millions" multiplier SEC noted on the statement (1, 1000, 1000000).
    /// The values in <see cref="Payload"/> are already de-scaled to whole units; this is kept
    /// for reference and to round-trip the issuer's stated presentation scale.
    /// </summary>
    public long Scale { get; set; } = 1;

    /// <summary>
    /// The reconstructed statement as JSON: <c>{ columns: [...], rows: [...] }</c>, where each
    /// row carries the issuer label, indent depth, abstract/total flags and a per-column value
    /// list aligned to <c>columns</c>. Stored as <c>jsonb</c> — read whole by the render
    /// surfaces, queryable if needed. Empty string until the parse step fills it.
    /// </summary>
    [Required(AllowEmptyStrings = true)]
    [Column(TypeName = "jsonb")]
    public string Payload { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
