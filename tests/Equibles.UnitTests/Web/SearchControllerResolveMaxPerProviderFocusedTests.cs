using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class SearchControllerResolveMaxPerProviderFocusedTests
{
    // Sibling to SearchControllerResolveMaxPerProviderOverviewTests (null
    // category → 6, overview cap). This pin covers the structurally
    // distinct FOCUSED arm of the ternary:
    //   IsNullOrWhiteSpace(category) ? Overview(6) : Focused(50)
    //                                                ^ this branch
    //
    // The contract: when a category is selected (non-null/empty/whitespace
    // string), return MaxPerProviderFocused (50) — the per-provider hit
    // cap on the "See all" / single-category search page, sized to feel
    // like a list rather than the overview's six-per-side cap.
    //
    // Why this pin is needed alongside the overview sibling: the existing
    // overview pin asserts EXACTLY 6 on null input. It cannot see:
    //   • A SWAP regression that returns Overview(6) on a non-empty
    //     category — `IsNullOrWhiteSpace ? Focused : Overview` flips
    //     the ternary; the overview test still passes (null → Focused
    //     would assert wrong, but if the swap is `non-empty → Overview`,
    //     null returns Focused=50, not 6). Hmm, this is wrong reasoning.
    //     Let me reconsider — the overview pin asserts null → 6. If
    //     swapped, null → 50, that pin fails. So the swap IS caught by
    //     the overview sibling.
    //
    //   • A CONSTANT-CHANGE regression that touches ONLY the Focused
    //     constant — `MaxPerProviderFocused = 100` (an operator
    //     experimenting with cap size) — would compile, pass the
    //     overview pin (Overview still = 6, unchanged), and silently
    //     overload the focused search page with 100 hits per provider
    //     (page-render slowdown, vertical-scroll fatigue). Only an
    //     assertion on the EXACT focused-cap value (50) catches this.
    //
    //   • A DROP-the-focused-arm regression — someone "simplifies" the
    //     ternary to just `return MaxPerProviderOverview` (under the
    //     intuition that "we only have one cap, use it everywhere") —
    //     would compile, pass the overview pin (still 6 on null), and
    //     shrink every focused page to 6 hits per provider, making the
    //     "See all" link behave identically to the overview page (UX
    //     dead-end).
    //
    // Pin: invoke with a real-shaped category ("documents" — the
    // SearchController's actual focused-category values include
    // documents/stocks/people/institutions per the SearchAggregator
    // wiring) and assert EXACTLY 50. Reflection-invoke since private
    // static.
    //
    // The pair (overview → 6 + focused → 50) defends both arms of the
    // ternary individually; any single-arm constant change or
    // single-arm drop surfaces at the corresponding sibling.
    [Fact]
    public void ResolveMaxPerProvider_NonEmptyCategory_ReturnsFocusedCapOfFifty()
    {
        var method = typeof(SearchController).GetMethod(
            "ResolveMaxPerProvider",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (int)method!.Invoke(null, ["documents"]);

        result.Should().Be(50);
    }
}
