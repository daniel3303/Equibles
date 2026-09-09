using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class FiscalYearEndValueParserTests
{
    private const string Modern = "http://www.xbrl.org/inlineXBRL/transformation/2020-02-12";
    private const string Legacy = "http://www.xbrl.org/inlineXBRL/transformation/2015-02-26";

    [Theory]
    [InlineData("03/31", "date-month-day", Modern, 3, 31)]
    [InlineData("12-31", "date-month-day", Modern, 12, 31)]
    [InlineData("2/29", "date-month-day", Modern, 2, 29)]
    [InlineData("31/3", "date-day-month", Modern, 3, 31)]
    [InlineData("March 31", "date-monthname-day-en", Modern, 3, 31)]
    [InlineData("March\u00a031", "date-monthname-day-en", Modern, 3, 31)]
    [InlineData("DEC. 31st", "date-monthname-day-en", Modern, 12, 31)]
    [InlineData("May 31", "datemonthdayen", Legacy, 5, 31)]
    [InlineData("June 30", "datemonthdayen", Legacy, 6, 30)]
    [InlineData("31 March", "date-day-monthname-en", Modern, 3, 31)]
    [InlineData("--07-31", null, null, 7, 31)]
    public void DecodesDeclaredRecurringDate(
        string value,
        string format,
        string registry,
        int month,
        int day
    )
    {
        FiscalYearEndValueParser.TryParse(value, out var date, format, registry).Should().BeTrue();
        date.Month.Should().Be(month);
        date.Day.Should().Be(day);
    }

    [Theory]
    [InlineData("03/31", "date-month-day", "https://example.com/2020-02-12")]
    [InlineData("03/31", "date-month-day", Legacy)]
    [InlineData("March 31", "datemonthdayen", Modern)]
    [InlineData("March 31", "unknown", Modern)]
    [InlineData("03/31", "date-month-day", null)]
    [InlineData("03/31", null, null)]
    [InlineData("--03-31", "date-month-day", Modern)]
    [InlineData("02/30", "date-month-day", Modern)]
    [InlineData("March 31 2026", "date-monthname-day-en", Modern)]
    [InlineData("13/01", "date-month-day", Modern)]
    [InlineData("31/03", "date-month-day", Modern)]
    public void RefusesUnknownOrInvalidTransform(string value, string format, string registry)
    {
        FiscalYearEndValueParser.TryParse(value, out _, format, registry).Should().BeFalse();
    }
}
