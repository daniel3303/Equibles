using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Schedule13DGTextValuesTests
{
    [Theory]
    [InlineData("67066G104", true)]
    [InlineData("67066G105", false)]
    [InlineData("03828A101", true)]
    [InlineData("000000000", true)]
    [InlineData("10568F2083", false)]
    [InlineData("MULTIPLES", false)]
    [InlineData("67066G10", false)]
    public void IsValidCusip_ChecksLengthAndCheckDigit(string cusip, bool valid) =>
        Schedule13DGTextValues.IsValidCusip(cusip).Should().Be(valid);

    [Theory]
    [InlineData("162856513", 162_856_513L)]
    [InlineData(" 6,020,981(1) ", 6_020_981L)]
    [InlineData(": 26,190,527 shares", 26_190_527L)]
    [InlineData("-0-", 0L)]
    [InlineData("None", 0L)]
    [InlineData("* 0 (2)", 0L)]
    [InlineData("1,624,818.00", 1_624_818L)]
    [InlineData("(See Item 5) 40(1)", 40L)]
    [InlineData("SHARES 0 BENEFICIALLY", 0L)]
    public void TryReadShares_ReadsTheFirstAnswer(string segment, long expected)
    {
        Schedule13DGTextValues.TryReadShares(segment, out var shares).Should().BeTrue();
        shares.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("See Item 4")]
    [InlineData("See Row 6 above")]
    [InlineData("280,0001")]
    [InlineData("1 234 567")]
    [InlineData("7.3%")]
    [InlineData("N/A")]
    public void TryReadShares_RefusesWhatIsNotACount(string segment) =>
        Schedule13DGTextValues.TryReadShares(segment, out _).Should().BeFalse();

    [Theory]
    [InlineData("", 0L)]
    [InlineData("N/A", 0L)]
    [InlineData("Not applicable", 0L)]
    [InlineData("------------ BENEFICIALLY", 0L)]
    [InlineData("1,409,193", 1_409_193L)]
    public void TryReadPower_ReadsABlankRowAsNoPower(string segment, long expected)
    {
        Schedule13DGTextValues.TryReadPower(segment, out var shares).Should().BeTrue();
        shares.Should().Be(expected);
    }

    [Fact]
    public void TryReadPower_RefusesAReference() =>
        Schedule13DGTextValues.TryReadPower("See item 5", out _).Should().BeFalse();

    [Theory]
    [InlineData("7.3%", "7.3")]
    [InlineData(" 6.5 %", "6.5")]
    [InlineData("6.6%(3)", "6.6")]
    [InlineData("Approximately 5.2%", "5.2")]
    [InlineData("0%", "0")]
    [InlineData(".5%", "0.5")]
    public void TryReadPercent_ReadsTheStatedPercent(string segment, string expected)
    {
        Schedule13DGTextValues.TryReadPercent(segment, out var percent).Should().BeTrue();
        percent
            .Should()
            .Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TryReadPercent_BoundStatesNoValue()
    {
        Schedule13DGTextValues.TryReadPercent("Less than 1%", out var percent).Should().BeTrue();
        percent.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("See Item 4")]
    [InlineData("101%")]
    public void TryReadPercent_RefusesWhatIsNotAPercent(string segment) =>
        Schedule13DGTextValues.TryReadPercent(segment, out _).Should().BeFalse();

    [Theory]
    [InlineData("December 31, 2023", 2023, 12, 31)]
    [InlineData("Dec. 31, 2023", 2023, 12, 31)]
    [InlineData("February 14, 2024________", 2024, 2, 14)]
    [InlineData("31 December 2023", 2023, 12, 31)]
    [InlineData("1/17/2024", 2024, 1, 17)]
    [InlineData("12/31/23", 2023, 12, 31)]
    public void FindDates_ReadsFiledDateShapes(string text, int year, int month, int day) =>
        Schedule13DGTextValues
            .FindDates(text)
            .Should()
            .ContainSingle()
            .Which.Date.Should()
            .Be(new DateOnly(year, month, day));

    [Theory]
    [InlineData("31/12/2023")]
    [InlineData("February 30, 2024")]
    public void FindDates_SkipsImpossibleDates(string text) =>
        Schedule13DGTextValues.FindDates(text).Should().BeEmpty();

    [Theory]
    [InlineData("[X]", true)]
    [InlineData("[ x ]", true)]
    [InlineData("☒", true)]
    [InlineData("þ", true)]
    [InlineData("ý", true)]
    [InlineData("x", true)]
    [InlineData("[ ]", false)]
    [InlineData("[_]", false)]
    [InlineData("☐", false)]
    [InlineData("¨", false)]
    [InlineData("o", false)]
    [InlineData("?", null)]
    public void ReadCheckbox_KnowsFiledGlyphs(string mark, bool? expected) =>
        Schedule13DGTextValues.ReadCheckbox(mark).Should().Be(expected);
}
