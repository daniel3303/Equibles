using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Schedule13DGFrontMatterReaderTests
{
    private const string Instruction =
        "Check the appropriate box to designate the rule pursuant to which this Schedule is filed: ";

    [Theory]
    [InlineData("[X] Rule 13d-1(b) [ ] Rule 13d-1(c) [ ] Rule 13d-1(d) *The remainder", "b")]
    [InlineData("Rule 13d-1(b) [ ] Rule 13d-1(c) [X] Rule 13d-1(d) [ ] *The remainder", "c")]
    [InlineData("☐ ☐ ☒ Rule 13d-1(b) Rule 13d-1(c) Rule 13d-1(d) *The remainder", "d")]
    [InlineData("x Rule 13d-1(b) o Rule 13d-1(c) o Rule 13d-1(d) *The remainder", "b")]
    public void ReadRules_PairsEachMarkWithItsCaption(string boxes, string rule)
    {
        var flat = Instruction + boxes;

        Schedule13DGFrontMatterReader
            .ReadRules(flat, flat.Length)
            .Should()
            .Equal($"Rule 13d-1({rule})");
    }

    [Theory]
    [InlineData("? Rule 13d-1(b) ? Rule 13d-1(c) ? Rule 13d-1(d) *The remainder")]
    [InlineData("☐ Rule 13d-1(b) ☒ Rule 13d-1(c) ☐ Rule 13d-1(d) ☐ more")]
    [InlineData("Rule 13d-1(b) Rule 13d-1(c)")]
    public void ReadRules_UnpairableMarks_ReadNothing(string boxes)
    {
        var flat = Instruction + boxes;

        Schedule13DGFrontMatterReader.ReadRules(flat, flat.Length).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Common Stock ---------- 67066G104 ---------- (CUSIP Number)")]
    [InlineData("CUSIP Number: 67066G104 Date of Event")]
    [InlineData("CUSIP No. 67066G 10 4 Page 2")]
    [InlineData("Cusip #67066G104 Check")]
    public void ReadCusips_TakesTheCusipNextToItsCaption(string flat) =>
        Schedule13DGFrontMatterReader.ReadCusips(flat, flat.Length).Should().Equal("67066G104");

    [Fact]
    public void ReadCusips_SedolCaption_IsNoCusip()
    {
        const string flat = "Common Stock ------ 2836634 ------ (SEDOL Number) December 31, 2023";

        Schedule13DGFrontMatterReader.ReadCusips(flat, flat.Length).Should().BeEmpty();
    }

    [Theory]
    [InlineData(
        "67066G104 (CUSIP Number) December 31, 2023 ---- (Date of Event Which Requires Filing of this Statement) Check"
    )]
    [InlineData("Date of Event Which Requires Filing of this Statement: December 31, 2023 Check")]
    public void ReadEventDate_TakesTheDateNextToItsCaption(string flat) =>
        Schedule13DGFrontMatterReader
            .ReadEventDate(flat, flat.Length)
            .Should()
            .Be((new DateOnly(2023, 12, 31), null));

    [Fact]
    public void ReadEventDate_DifferentDatesOnEitherSide_IsRefused()
    {
        const string flat =
            "December 31, 2023 (Date of Event Which Requires Filing of this Statement) January 5, 2024";

        Schedule13DGFrontMatterReader
            .ReadEventDate(flat, flat.Length)
            .Should()
            .Be((null, "two event dates"));
    }

    [Fact]
    public void ReadEventDate_DateSeparatedByOtherText_IsNotTheAnswer()
    {
        const string flat =
            "Filed on March 1, 2024 by the person named below (Date of Event Which Requires Filing of this Statement) Check";

        Schedule13DGFrontMatterReader
            .ReadEventDate(flat, flat.Length)
            .Should()
            .Be((null, "no event date"));
    }
}
