using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserParseTransactionCodeUnknownTests
{
    // ParseTransactionCode maps the known Form 4 codes (P/S/A/M/X/F/E/G/I/W) and routes
    // everything else through the catch-all. A code outside that set — here "J", a real SEC
    // code the parser doesn't model — must degrade to Other, never throw, so ingestion of a
    // filing carrying an unmodelled code keeps working. Oracle from the contract, not the body.
    [Fact]
    public void ParseTransactionCode_UnrecognizedCode_MapsToOther()
    {
        var result = InsiderFilingParser.ParseTransactionCode("J");

        result.Should().Be(TransactionCode.Other);
    }
}
