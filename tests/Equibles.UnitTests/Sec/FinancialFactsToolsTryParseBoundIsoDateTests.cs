using System.Reflection;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsTryParseBoundIsoDateTests
{
    // Contract (FinancialFactsTools.TryParseBound inline comment): "Accepts
    // an absent bound (null/blank → no bound) but rejects a non-empty value
    // that is not an ISO yyyy-MM-dd date." Three reachable outcomes:
    //   • blank → (true, null)                 — pinned by sibling
    //   • non-blank, non-ISO → (false, null)   — pinned by sibling
    //   • non-blank, valid ISO → (true, parsed date) — THIS pin
    //
    // The valid-ISO arm is the production happy path: every LLM-driven
    // bounded fact query (`GetFinancialFact("AAPL", "revenue",
    // from: "2020-01-01")`) flows through this arm. The two existing
    // sibling pins both prove the OUT parameter is null — neither
    // proves a valid date actually round-trips into the out parameter.
    //
    // The risks this pin uniquely catches:
    //   • A "simplify" refactor that drops the second TryParseExact
    //     block under the false intuition "blank short-circuits, anything
    //     else is rejection" would compile, pass the blank pin (still
    //     short-circuits at IsNullOrWhiteSpace), pass the non-ISO pin
    //     (falls through to `return false`), and silently REJECT every
    //     bounded fact query — every "from" / "to" parameter sent by an
    //     LLM would surface as "Unknown date format" to the caller while
    //     the unbounded "list everything for this concept" path keeps
    //     working. The failure mode is invisible from the existing pins.
    //
    //   • A swap of the out-parameter assignment — `bound = default` or
    //     a hard-coded date — would compile cleanly. The existing pins
    //     can't distinguish "the parsed value flowed through" from "any
    //     non-null value reached the out parameter" because they both
    //     assert null.
    //
    //   • Format-string drift — a refactor that loosened the format to
    //     accept multiple shapes (e.g. an array of patterns) might
    //     compile and pass both siblings while silently changing which
    //     dates parse. Asserting an exact DateOnly equality on the
    //     out parameter catches any value-drift regression.
    //
    // Pin: TryParseBound("2024-03-15", out var bound) returns (true,
    // 2024-03-15). The dual assertion (success AND exact parsed value)
    // covers the full happy-path contract.
    [Fact]
    public void TryParseBound_ValidIsoDate_ReturnsTrueAndPopulatesBoundWithParsedDate()
    {
        var method = typeof(FinancialFactsTools).GetMethod(
            "TryParseBound",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "2024-03-15", null };
        var result = (bool)method!.Invoke(null, args);

        result.Should().BeTrue();
        ((DateOnly?)args[1]).Should().Be(new DateOnly(2024, 3, 15));
    }
}
