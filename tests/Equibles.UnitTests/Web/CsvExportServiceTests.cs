using System.Globalization;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class CsvExportServiceTests
{
    [Fact]
    public void EscapeField_PlainText_ReturnsUnquoted()
    {
        CsvExportService.EscapeField("AAPL").Should().Be("AAPL");
    }

    [Fact]
    public void EscapeField_Empty_ReturnsEmpty()
    {
        CsvExportService.EscapeField(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void EscapeField_Null_ReturnsEmpty()
    {
        CsvExportService.EscapeField(null).Should().Be(string.Empty);
    }

    [Fact]
    public void EscapeField_ContainsComma_WrapsInQuotes()
    {
        CsvExportService.EscapeField("Apple, Inc.").Should().Be("\"Apple, Inc.\"");
    }

    [Fact]
    public void EscapeField_ContainsDoubleQuote_DoublesQuoteAndWraps()
    {
        // RFC-4180: a `"` inside the field becomes `""`, and the whole field is quoted.
        CsvExportService
            .EscapeField("Acme \"Special\" Co.")
            .Should()
            .Be("\"Acme \"\"Special\"\" Co.\"");
    }

    [Fact]
    public void EscapeField_ContainsNewline_WrapsInQuotes()
    {
        CsvExportService.EscapeField("Line1\nLine2").Should().Be("\"Line1\nLine2\"");
    }

    [Fact]
    public void EscapeField_ContainsCarriageReturn_WrapsInQuotes()
    {
        CsvExportService.EscapeField("Line1\rLine2").Should().Be("\"Line1\rLine2\"");
    }

    [Fact]
    public void Format_LongCultureInvariant_NoThousandSeparator()
    {
        // Pinning culture-invariance: even on a host with PT-PT default culture (uses
        // space as thousand separator, comma as decimal) the output stays "1500000".
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-PT");
            CsvExportService.Format(1_500_000L).Should().Be("1500000");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void Format_DateOnly_UsesIsoFormat()
    {
        CsvExportService.Format(new DateOnly(2024, 6, 30)).Should().Be("2024-06-30");
    }

    [Fact]
    public void BuildCsv_HeadersAndRows_EmitsCanonicalCsv()
    {
        var csv = CsvExportService.BuildCsv(
            ["Ticker", "Name", "Shares"],
            new[]
            {
                new[] { "AAPL", "Apple, Inc.", "10000" },
                new[] { "MSFT", "Microsoft", "20000" },
            }
        );

        csv.Should()
            .Be("Ticker,Name,Shares\n" + "AAPL,\"Apple, Inc.\",10000\n" + "MSFT,Microsoft,20000\n");
    }

    [Fact]
    public void BuildCsv_NoRows_StillEmitsHeader()
    {
        var csv = CsvExportService.BuildCsv(["Ticker"], []);

        csv.Should().Be("Ticker\n");
    }

    [Fact]
    public void Format_Decimal_CultureInvariant_UsesPeriodDecimalSeparator()
    {
        // Format(long) is pinned for invariant culture but Format(decimal) is not. A
        // regression that dropped CultureInfo.InvariantCulture from the decimal overload
        // would render "123,45" on a comma-decimal locale like de-DE — that corrupts
        // CSV (the comma becomes a column separator).
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            CsvExportService.Format(123.45m).Should().Be("123.45");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void Format_Double_CultureInvariant_UsesPeriodDecimalSeparator()
    {
        // The remaining Format overload not yet pinned for invariant culture. Same risk
        // as the decimal overload: a regression that dropped InvariantCulture from the
        // double overload would render 1.5 as "1,5" on de-DE and the comma would corrupt
        // the surrounding CSV row.
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            CsvExportService.Format(1.5).Should().Be("1.5");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }
}
