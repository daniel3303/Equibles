using System.Xml;
using System.Xml.Linq;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

/// <summary>
/// Reads a filing's <c>FilingSummary.xml</c> — SEC's index of the R-files (rendered statement
/// tables) it produced. Used by capture to decide which <c>R#.htm</c> files to fetch, and by the
/// parse step to map each captured R-file back to its role and section.
/// </summary>
public static class FilingSummaryParser
{
    private const string StatementsCategory = "Statements";

    /// <summary>Every <c>&lt;Report&gt;</c> entry, in document order. Empty when the XML is absent or malformed.</summary>
    public static List<FilingSummaryReport> ParseReports(string filingSummaryXml)
    {
        if (string.IsNullOrWhiteSpace(filingSummaryXml))
        {
            return [];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(filingSummaryXml);
        }
        catch (XmlException)
        {
            return [];
        }

        return document
            .Descendants("Report")
            .Select(report => new FilingSummaryReport
            {
                ShortName = Value(report, "ShortName"),
                LongName = Value(report, "LongName"),
                Role = Value(report, "Role"),
                HtmlFileName = Value(report, "HtmlFileName"),
                MenuCategory = Value(report, "MenuCategory"),
                Position = ParsePosition(Value(report, "Position")),
            })
            .ToList();
    }

    /// <summary>
    /// The financial-statement reports only — section <c>Statements</c> with a rendered R-file —
    /// in filing order. Cover page and notes are excluded; these are the statements we reconstruct.
    /// </summary>
    public static List<FilingSummaryReport> StatementReports(string filingSummaryXml) =>
        ParseReports(filingSummaryXml)
            .Where(report =>
                string.Equals(
                    report.MenuCategory,
                    StatementsCategory,
                    StringComparison.OrdinalIgnoreCase
                ) && !string.IsNullOrWhiteSpace(report.HtmlFileName)
            )
            .OrderBy(report => report.Position)
            .ToList();

    private static string Value(XElement report, string element)
    {
        var value = report.Element(element)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int ParsePosition(string value) =>
        int.TryParse(value, out var position) ? position : 0;
}
