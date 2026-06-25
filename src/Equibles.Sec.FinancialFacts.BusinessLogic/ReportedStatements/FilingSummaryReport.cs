namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

/// <summary>
/// One <c>&lt;Report&gt;</c> entry from a filing's <c>FilingSummary.xml</c> — SEC's index of the
/// rendered R-files it produced for the filing. Identifies which <c>R#.htm</c> table holds which
/// statement, in what order, under which role.
/// </summary>
public class FilingSummaryReport
{
    /// <summary>Human title SEC renders for the report, e.g. "CONSOLIDATED STATEMENTS OF OPERATIONS".</summary>
    public string ShortName { get; set; }

    /// <summary>Qualified title, e.g. "0000003 - Statement - CONSOLIDATED STATEMENTS OF OPERATIONS".</summary>
    public string LongName { get; set; }

    /// <summary>The issuer's role URI for the report — stable identity within the filing.</summary>
    public string Role { get; set; }

    /// <summary>The rendered table file, e.g. <c>R2.htm</c>. Absent on a few index-only entries.</summary>
    public string HtmlFileName { get; set; }

    /// <summary>Section the report belongs to: <c>Cover</c>, <c>Statements</c>, <c>Notes</c>, …</summary>
    public string MenuCategory { get; set; }

    /// <summary>The report's order within the filing.</summary>
    public int Position { get; set; }
}
