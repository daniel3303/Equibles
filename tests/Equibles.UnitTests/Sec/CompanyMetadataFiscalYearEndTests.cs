using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// SEC's submissions <c>fiscalYearEnd</c> is a free-form "MMDD" string and is
/// occasionally blank or malformed. A silently-wrong month would corrupt every
/// downstream fiscal-quarter calculation, so parsing must be strict: exactly
/// four digits forming a valid 1-12 month and 1-31 day, or null.
/// </summary>
public class CompanyMetadataFiscalYearEndTests
{
    [Theory]
    [InlineData("0928", 9, 28)] // Apple
    [InlineData("0630", 6, 30)] // Microsoft
    [InlineData("1231", 12, 31)] // calendar-year filer
    [InlineData("0101", 1, 1)]
    [InlineData("0229", 2, 29)] // leap-day fiscal year-end is a real date
    [InlineData(" 0928 ", 9, 28)] // SEC pads/whitespaces some values
    public void FiscalYearEnd_ValidMmdd_ParsesMonthAndDay(string raw, int month, int day)
    {
        var metadata = new CompanyMetadata { FiscalYearEnd = raw };

        metadata.FiscalYearEndMonth.Should().Be(month);
        metadata.FiscalYearEndDay.Should().Be(day);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("928")] // not 4 chars
    [InlineData("12311")] // too long
    [InlineData("0000")] // month 0, day 0
    [InlineData("1332")] // month 13, day 32
    [InlineData("0931")] // Sept has 30 days — impossible calendar date
    [InlineData("0230")] // Feb never has 30 days
    [InlineData("0431")] // April has 30 days
    [InlineData("ab12")] // non-numeric
    [InlineData("09-8")] // non-numeric separator
    public void FiscalYearEnd_MissingOrMalformed_YieldsNull(string raw)
    {
        var metadata = new CompanyMetadata { FiscalYearEnd = raw };

        metadata.FiscalYearEndMonth.Should().BeNull();
        metadata.FiscalYearEndDay.Should().BeNull();
    }

    [Fact]
    public void FiscalYearEnd_MonthInRangeDayOutOfRange_YieldsNull()
    {
        var metadata = new CompanyMetadata { FiscalYearEnd = "0932" };

        metadata.FiscalYearEndMonth.Should().BeNull();
        metadata.FiscalYearEndDay.Should().BeNull();
    }
}
