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

    /// <summary>The parsed period end carried by <see cref="Label"/>.</summary>
    public DateOnly? PeriodEnd { get; set; }

    /// <summary>
    /// The column's proven ISO monetary unit: explicit header currency first, otherwise the title's
    /// single unambiguous currency. Null is ambiguous and consumers must fail closed.
    /// </summary>
    public string Currency { get; set; }

    /// <summary>
    /// The exact whole-money multiplier for this column. Null means the title/header carried
    /// conflicting currency-specific scales and consumers must not publish monetary values.
    /// </summary>
    public long? Scale { get; set; }

    /// <summary>
    /// The exact per-share multiplier for this column. Null means currency-specific per-share
    /// clauses conflict or do not prove a scale for this column.
    /// </summary>
    public long? PerShareScale { get; set; }

    /// <summary>The duration group as filed, e.g. <c>"3 Months Ended"</c>; null for a point-in-time (balance sheet) column.</summary>
    public string Duration { get; set; }

    /// <summary>True for a point-in-time column (balance sheet); false for a duration column (income / cash flow).</summary>
    public bool IsInstant { get; set; }
}
