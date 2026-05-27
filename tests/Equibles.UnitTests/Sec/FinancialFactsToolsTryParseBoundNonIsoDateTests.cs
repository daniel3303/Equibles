using System.Reflection;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsTryParseBoundNonIsoDateTests
{
    [Fact]
    public void TryParseBound_NonIsoDate_ReturnsFalseAndLeavesBoundNull()
    {
        // TryParseBound gates fromDate/toDate inputs on the GetFinancialFact and
        // CompareFinancialFact MCP tools. The in-source comment makes the contract
        // explicit:
        //   "Accepts an absent bound (null/blank → no bound) but rejects a non-empty
        //    value that is not an ISO yyyy-MM-dd date, so a typo can't silently widen
        //    the result set."
        // The risk this catches: a refactor that swapped TryParseExact for TryParse
        // would start accepting culturally-formatted dates ("01/15/2024", "15-01-2024",
        // "15.01.2024") and silently widen the queried date range — financial-fact
        // queries returning facts outside the date window the LLM was asked about.
        //
        // Pin the rejection arm: a slash-separated date (a realistic typo for ISO
        // dashes) must return false AND leave the out bound null, per the TryParse
        // convention. Both signals matter — a caller checks the bool to decide
        // whether to report the error, and the null bound prevents partial-state bugs
        // if the caller forgets to check the bool.
        var method = typeof(FinancialFactsTools).GetMethod(
            "TryParseBound",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "2024/01/15", null };
        var result = (bool)method.Invoke(null, args);

        result.Should().BeFalse();
        ((DateOnly?)args[1]).Should().BeNull();
    }
}
