using System.Reflection;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsTryParseBoundBlankTests
{
    [Fact]
    public void TryParseBound_BlankInput_ReturnsTrueAndLeavesBoundNull()
    {
        // Sibling to the existing NonIsoDate rejection pin. The in-source
        // comment is explicit: "Accepts an absent bound (null/blank → no
        // bound) but rejects a non-empty value that is not an ISO yyyy-MM-dd
        // date". The rejection pin covers "non-empty malformed"; this pins
        // the structurally-distinct "absent" arm — the LLM omits an
        // optional bound by passing blank, and the tool must interpret
        // that as "no filter" (success, null bound), NOT as "parse error".
        // A refactor that flips IsNullOrWhiteSpace to `return false` (under
        // "blank means failed parse, surface the error to the LLM") would
        // compile, pass the rejection pin, and refuse every unbounded
        // financial-fact query — the dominant use case.
        var method = typeof(FinancialFactsTools).GetMethod(
            "TryParseBound",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "   ", null };
        var result = (bool)method!.Invoke(null, args);

        result.Should().BeTrue();
        ((DateOnly?)args[1]).Should().BeNull();
    }
}
