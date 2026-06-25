namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// The reconstructed body of an as-reported statement — the period columns and the line-item
/// rows — serialized to <c>ReportedFinancialStatement.Payload</c> (jsonb) and read whole by the
/// render surfaces. Values are stored as the issuer presented them (e.g. "$ in Millions"); the
/// presentation scale is conveyed by <see cref="ScaleNote"/> rather than being applied, so the
/// statement renders exactly as filed.
/// </summary>
public class ReportedStatementPayload
{
    /// <summary>The issuer's scale / currency note, e.g. <c>"USD ($) — $ in Millions, shares in Thousands"</c>; null if none.</summary>
    public string ScaleNote { get; set; }

    /// <summary>The period columns, in the order the issuer presented them (newest first).</summary>
    public List<ReportedStatementColumn> Columns { get; set; } = [];

    /// <summary>The line-item rows, in filing order.</summary>
    public List<ReportedStatementRow> Rows { get; set; } = [];
}
