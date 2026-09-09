using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlFiscalYearEndTests
{
    private static string Envelope(
        string value = "--12-31",
        string ns = "http://xbrl.sec.gov/dei/2026",
        string segment = ""
    ) =>
        $$"""
            <html xmlns:dei="{{ns}}"><body><ix:header>
            <xbrli:context id="d_2026-01-01_2026-03-31">
              <xbrli:entity><xbrli:identifier scheme="http://www.sec.gov/CIK">0001274173</xbrli:identifier>{{segment}}</xbrli:entity>
              <xbrli:period><xbrli:startDate>2026-01-01</xbrli:startDate><xbrli:endDate>2026-03-31</xbrli:endDate></xbrli:period>
            </xbrli:context></ix:header>
            <ix:nonNumeric contextRef="d_2026-01-01_2026-03-31" name="dei:CurrentFiscalYearEndDate">{{value}}</ix:nonNumeric>
            </body></html>
            """;

    [Fact]
    public void ReadsJhgHistoricalCalendarFromItsOwnConsolidatedContext()
    {
        var actual = new InlineXbrlParser()
            .ParseEnvelope(Envelope())
            .FiscalYearEnds.Should()
            .ContainSingle()
            .Which;
        actual.Cik.Should().Be("0001274173");
        actual.PeriodEnd.Should().Be(new DateOnly(2026, 3, 31));
        actual.Month.Should().Be(12);
        actual.Day.Should().Be(31);
    }

    [Theory]
    [InlineData("--02-30", "http://xbrl.sec.gov/dei/2026", "")]
    [InlineData("--12-31", "https://example.com/dei/2026", "")]
    [InlineData("--12-31", "http://xbrl.sec.gov/dei/2026", "<xbrli:segment></xbrli:segment>")]
    public void RefusesUnusableOrUnqualifiedEvidence(string value, string ns, string segment)
    {
        new InlineXbrlParser()
            .ParseEnvelope(Envelope(value, ns, segment))
            .FiscalYearEnds.Should()
            .BeEmpty();
    }

    [Theory]
    [InlineData("xsi:nil=\"true\"")]
    [InlineData("xsi:nil=\"1\"")]
    [InlineData("format=\"ixt:date-month-day\"")]
    public void RefusesNilOrUndeclaredTransform(string attribute)
    {
        var html = Envelope().Replace("<ix:nonNumeric ", $"<ix:nonNumeric {attribute} ");
        new InlineXbrlParser().ParseEnvelope(html).FiscalYearEnds.Should().BeEmpty();
    }

    [Theory]
    [InlineData("03/31", "date-month-day", 3, 31)]
    [InlineData("March&#160;31", "date-monthname-day-en", 3, 31)]
    public void ReadsRegisteredTransformWithSourceNamespace(
        string value,
        string format,
        int month,
        int day
    )
    {
        var html = Envelope(value)
            .Replace(
                "<html ",
                "<html xmlns:transform=\"http://www.xbrl.org/inlineXBRL/transformation/2020-02-12\" "
            )
            .Replace("<ix:nonNumeric ", $"<ix:nonNumeric format=\"transform:{format}\" ");
        var actual = new InlineXbrlParser()
            .ParseEnvelope(html)
            .FiscalYearEnds.Should()
            .ContainSingle()
            .Which;
        actual.Month.Should().Be(month);
        actual.Day.Should().Be(day);
        var shadowed = html.Replace(
            "<ix:nonNumeric ",
            "<ix:nonNumeric xmlns:transform=\"https://example.com/untrusted\" "
        );
        new InlineXbrlParser().ParseEnvelope(shadowed).FiscalYearEnds.Should().BeEmpty();
    }
}
