using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserParseBoolZeroTests
{
    // SEC Form 4 boolean flags (isDirector, isOfficer, isTenPercentOwner, …) arrive as "1"/"0".
    // ParseBool must treat only the affirmative tokens as true; a value of "0" — a flag explicitly
    // disabled — must read as false. An over-permissive regression (e.g. !IsNullOrEmpty) would flip
    // "0" to true and corrupt every insider-role flag. Oracle from the contract, not the body.
    [Fact]
    public void ParseBool_ZeroValue_ReturnsFalse()
    {
        InsiderFilingParser.ParseBool("0").Should().BeFalse();
    }
}
