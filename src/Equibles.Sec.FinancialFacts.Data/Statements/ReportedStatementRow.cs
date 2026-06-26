namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// One line of an as-reported statement, exactly as the issuer filed it: their label, indent
/// depth, abstract/total flags, the XBRL concept SEC tagged it with, and the value per period
/// column. <see cref="Values"/> aligns positionally to the statement's
/// <see cref="ReportedStatementColumn"/> list; a null entry is a cell the issuer left blank
/// (e.g. an abstract section header).
/// </summary>
public class ReportedStatementRow
{
    /// <summary>The issuer's line label as rendered, e.g. <c>"Research and development"</c>.</summary>
    public string Label { get; set; }

    /// <summary>XBRL taxonomy prefix SEC tagged the line with, e.g. <c>"us-gaap"</c> or a company prefix; null if untagged.</summary>
    public string Taxonomy { get; set; }

    /// <summary>XBRL concept local name, e.g. <c>"NetIncomeLoss"</c>; null if the line carries no concept.</summary>
    public string Concept { get; set; }

    /// <summary>Indent depth for display: 0 for a top-level line / section header / total, 1 for a line within a section.</summary>
    public int Depth { get; set; }

    /// <summary>A section header with no values of its own, e.g. <c>"Operating expenses:"</c>.</summary>
    public bool IsAbstract { get; set; }

    /// <summary>A subtotal / total line (rendered emphasized), e.g. <c>"Total current assets"</c>.</summary>
    public bool IsTotal { get; set; }

    /// <summary>The value per period column, aligned to <see cref="ReportedStatementColumn"/>; null = blank cell.</summary>
    public List<decimal?> Values { get; set; } = [];
}
