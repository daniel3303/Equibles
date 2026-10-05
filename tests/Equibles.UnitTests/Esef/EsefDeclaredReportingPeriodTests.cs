using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Esef;

public class EsefDeclaredReportingPeriodTests
{
    private const string Lei = "549300JUGBT2EH17X827";
    private static readonly DateOnly End = new(2025, 12, 31);

    [Fact]
    public void RecordedDfdsInterimReport_AnnualRollingFactsDoNotOverrideDeclaredSixMonthPeriod()
    {
        var html = File.ReadAllText(Asset("dfds-2026-interim-excerpt.xhtml"));
        var end = new DateOnly(2026, 6, 30);
        var facts = EsefInlineXbrlParser.Parse(html);

        EsefAnnualPeriod.IsProven(facts, "549300JZVW1Y1UZ5UK38", end).Should().BeTrue();
        EsefAnnualPeriod.IsProvenInline(html, "549300JZVW1Y1UZ5UK38", end).Should().BeFalse();
    }

    [Theory]
    [InlineData("annual", true)]
    [InlineData("interim", false)]
    [InlineData("different-end", false)]
    [InlineData("missing-start", false)]
    [InlineData("missing-end", false)]
    [InlineData("bad-date", false)]
    [InlineData("conflicting-start", false)]
    [InlineData("identical-duplicates", true)]
    [InlineData("unknown-context", false)]
    [InlineData("duplicate-context", false)]
    [InlineData("other-issuer", true)]
    [InlineData("untrusted-taxonomy", true)]
    [InlineData("aliased-taxonomy", true)]
    [InlineData("continued-date", false)]
    [InlineData("transformed-date", false)]
    [InlineData("nil-date", false)]
    [InlineData("nested-date", false)]
    [InlineData("bom", true)]
    public void DeclaredPeriod_RequiresConsistentOwnedDatesWithoutChangingFinancialProof(
        string shape,
        bool expected
    )
    {
        var html = File.ReadAllText(Asset("ennogie-2025-annual-excerpt.xhtml"));
        var start = Declaration("ReportingPeriodStartDate", "2025-01-01");
        var end = Declaration("ReportingPeriodEndDate", "2025-12-31");
        var declarations = start + end;
        switch (shape)
        {
            case "interim":
                declarations = declarations.Replace("2025-01-01", "2025-07-01");
                break;
            case "different-end":
                declarations = declarations.Replace("2025-12-31", "2025-06-30");
                break;
            case "missing-start":
                declarations = end;
                break;
            case "missing-end":
                declarations = start;
                break;
            case "bad-date":
                declarations = declarations.Replace("2025-01-01", "unknown");
                break;
            case "conflicting-start":
                declarations += Declaration("ReportingPeriodStartDate", "2025-07-01");
                break;
            case "identical-duplicates":
                declarations += declarations;
                break;
            case "unknown-context":
                declarations = declarations.Replace("ctx-2", "missing");
                break;
            case "duplicate-context":
                html = html.Replace(
                    "</ix:resources>",
                    Context("metadata", Lei) + Context("metadata", Lei) + "</ix:resources>"
                );
                declarations = declarations.Replace("ctx-2", "metadata");
                break;
            case "other-issuer":
                html = html.Replace(
                    "</ix:resources>",
                    Context("foreign", "549300JZVW1Y1UZ5UK38") + "</ix:resources>"
                );
                declarations = declarations
                    .Replace("ctx-2", "foreign")
                    .Replace("2025-01-01", "2025-07-01");
                break;
            case "untrusted-taxonomy":
                html = html.Replace("http://xbrl.dcca.dk/gsd", "https://example.org/gsd");
                break;
            case "aliased-taxonomy":
                html = html.Replace("xmlns:gsd=", "xmlns:report=");
                declarations = declarations.Replace("gsd:", "report:");
                break;
            case "continued-date":
                declarations = declarations.Replace(
                    "contextRef=",
                    "continuedAt=\"more\" contextRef="
                );
                break;
            case "transformed-date":
                declarations = declarations.Replace(
                    "contextRef=",
                    "format=\"ixt:date-day-month-year\" contextRef="
                );
                break;
            case "nil-date":
                declarations = declarations.Replace(
                    "contextRef=",
                    "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\" contextRef="
                );
                break;
            case "nested-date":
                declarations = declarations.Replace("2025-01-01", "<span>2025-01-01</span>");
                break;
            case "bom":
                html = "\uFEFF" + html;
                break;
        }
        html = html.Replace("</body>", declarations + "</body>");

        EsefAnnualPeriod.IsProvenInline(html, Lei, End).Should().Be(expected);
    }

    [Theory]
    [InlineData("2024-12-16", true)]
    [InlineData("2025-01-15", true)]
    [InlineData("2024-12-15", false)]
    [InlineData("2025-01-16", false)]
    public void DeclaredAnnualDates_KeepTheExistingDurationBounds(string start, bool expected)
    {
        var html = File.ReadAllText(Asset("ennogie-2025-annual-excerpt.xhtml"));
        html = html.Replace(
            "</body>",
            Declaration("ReportingPeriodStartDate", start)
                + Declaration("ReportingPeriodEndDate", "2025-12-31")
                + "</body>"
        );
        EsefAnnualPeriod.IsProvenInline(html, Lei, End).Should().Be(expected);
    }

    private static string Declaration(string name, string value) =>
        $"<ix:nonNumeric name=\"gsd:{name}\" contextRef=\"ctx-2\">{value}</ix:nonNumeric>";

    private static string Context(string id, string owner) =>
        $"""
            <xbrli:context id="{id}"><xbrli:entity><xbrli:identifier scheme="http://standards.iso.org/iso/17442">{owner}</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:startDate>2025-01-01</xbrli:startDate><xbrli:endDate>2025-12-31</xbrli:endDate></xbrli:period></xbrli:context>
            """;

    private static string Asset(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Esef", name);
}
