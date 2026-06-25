namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// One period column of an as-reported statement — a comparative the issuer presented side by
/// side (e.g. "3 Months Ended Mar. 28, 2026"). Cells in each <see cref="ReportedStatementRow"/>
/// align positionally to the statement's columns.
/// </summary>
public class ReportedStatementColumn
{
    /// <summary>The column's period-end label as filed, e.g. <c>"Mar. 28, 2026"</c>.</summary>
    public string Label { get; set; }

    /// <summary>The duration group as filed, e.g. <c>"3 Months Ended"</c>; null for a point-in-time (balance sheet) column.</summary>
    public string Duration { get; set; }

    /// <summary>True for a point-in-time column (balance sheet); false for a duration column (income / cash flow).</summary>
    public bool IsInstant { get; set; }
}
