using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

/// <summary>
/// The result of parsing one rendered statement R-file: the reconstructed
/// <see cref="ReportedStatementPayload"/> plus the metadata the parse step needs to populate a
/// <c>ReportedFinancialStatement</c> row — the primary (current) period the statement reports and
/// the issuer's presentation scale / currency.
/// </summary>
public class RFileStatement
{
    public ReportedStatementPayload Payload { get; set; }

    /// <summary>Period end of the primary (current, shortest-duration) column — the period this statement reports.</summary>
    public DateOnly PrimaryPeriodEnd { get; set; }

    /// <summary>Approximate period start of the primary column (end minus its duration; equals end for an instant).</summary>
    public DateOnly PrimaryPeriodStart { get; set; }

    /// <summary>True when the primary column is a point in time (balance sheet).</summary>
    public bool PrimaryIsInstant { get; set; }

    /// <summary>Reporting currency, e.g. <c>USD</c>; null if not stated in the title.</summary>
    public string Currency { get; set; }

    /// <summary>The "$ in Thousands/Millions" presentation multiplier (1 / 1000 / 1000000) from the title.</summary>
    public long Scale { get; set; } = 1;

    /// <summary>True when no statement table or no data rows were found — the R-file carried nothing usable.</summary>
    public bool IsEmpty => Payload == null || Payload.Rows.Count == 0;
}
