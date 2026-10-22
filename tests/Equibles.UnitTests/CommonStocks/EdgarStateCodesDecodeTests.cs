using Equibles.CommonStocks.Data.Helpers;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins EdgarStateCodes.Decode against the SEC's official EDGAR state-and-country code table.
/// The audit found raw internal codes ("X0", "M0", "C3") leaking into SearchInstitutions'
/// State/Country column, where an MCP consumer relays them verbatim. US state abbreviations
/// (and unknown/blank values) must pass through unchanged — only the digit-carrying EDGAR
/// codes decode.
/// </summary>
public class EdgarStateCodesDecodeTests
{
    [Theory]
    [InlineData("X0", "United Kingdom")]
    [InlineData("M0", "Japan")]
    [InlineData("C3", "Australia")]
    [InlineData("A6", "Ontario, Canada")]
    [InlineData("2M", "Germany")]
    [InlineData("F4", "China")]
    [InlineData("E9", "Cayman Islands")]
    public void Decode_KnownEdgarCode_ReturnsCountryName(string code, string expected)
    {
        EdgarStateCodes.Decode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("NE")] // Nebraska, NOT an EDGAR foreign code — must pass through
    [InlineData("NY")]
    [InlineData("CA")]
    public void Decode_UsStateAbbreviation_PassesThrough(string code)
    {
        EdgarStateCodes.Decode(code).Should().Be(code);
    }

    [Fact]
    public void Decode_UnknownOrBlank_PassesThrough()
    {
        EdgarStateCodes.Decode("Z9").Should().Be("Z9");
        EdgarStateCodes.Decode("").Should().Be("");
        EdgarStateCodes.Decode(null).Should().BeNull();
    }

    [Theory]
    [InlineData("DE")]
    [InlineData("tx")]
    [InlineData("DC")]
    [InlineData("PR")] // Puerto Rico
    [InlineData("1V")] // Northern Mariana Islands, filed under a country code
    [InlineData("X1")]
    public void IsUnitedStates_UsStateOrTerritory_IsTrue(string code)
    {
        EdgarStateCodes.IsUnitedStates(code).Should().BeTrue();
    }

    [Theory]
    [InlineData("A0")] // Alberta, Canada
    [InlineData("L2")] // Ireland
    [InlineData("E9")] // Cayman Islands
    [InlineData("Z9")] // unknown
    [InlineData("")]
    [InlineData(null)]
    public void IsUnitedStates_ForeignBlankOrUnknown_IsFalse(string code)
    {
        EdgarStateCodes.IsUnitedStates(code).Should().BeFalse();
    }
}
