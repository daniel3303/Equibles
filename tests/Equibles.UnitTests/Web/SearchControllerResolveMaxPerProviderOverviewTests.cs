using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class SearchControllerResolveMaxPerProviderOverviewTests
{
    // ResolveMaxPerProvider is the per-provider hit-count discriminator for
    // the search results page. Its contract is the ternary:
    //   IsNullOrWhiteSpace(category) ? MaxPerProviderOverview : MaxPerProviderFocused
    // — when no category is selected (the overview page), show 6 hits per
    // provider (so all providers fit side-by-side without scrolling); when
    // one category is selected (the focused / "See all" page), show 50.
    //
    // The risk this pin uniquely catches: ResolveMaxPerProvider is
    // private static and currently untested. The constants
    // MaxPerProviderOverview = 6 and MaxPerProviderFocused = 50 encode
    // a page-layout contract that any of these regressions would
    // silently break:
    //   • SWAP regression — `IsNullOrWhiteSpace(category) ? Focused :
    //     Overview` (logic flip) — would compile, and every overview
    //     page would show 50 results per provider (visual clutter,
    //     vertical-scroll required just to see the category headers),
    //     while every focused page would shrink to 6 results (too few
    //     to feel like a "see all" view). Both regressions are visible
    //     to a designer but would compile cleanly and survive any test
    //     that doesn't assert on the count.
    //   • DROP-the-guard regression — drops the null/whitespace check
    //     under "category is always supplied by the form" — would NRE
    //     on null category (instant-search.js's initial empty-query
    //     request, the "Show all categories" reset link, any caller
    //     that passes null intentionally).
    //   • CONSTANT-VALUE swap — someone "tidies" MaxPerProviderOverview
    //     from 6 to 5 or 8 — would compile, render off-grid layouts.
    //
    // The DUAL-SEMANTIC assertion (overview path returns EXACTLY 6)
    // distinguishes:
    //   • Working ternary: returns 6 on null/empty/whitespace category.
    //   • Logic-flip: returns 50 (fails).
    //   • Drop-the-guard: throws on null (fails the .NotThrow expectation
    //     implicit in the direct-cast invocation).
    //   • Constant change: returns the new value (fails the exact-6 assertion).
    //
    // Pin: invoke with null (the most adversarial input — exercises both
    // the null-check arm AND the overview-constant return). Reflection-
    // invoke since the helper is private static.
    [Fact]
    public void ResolveMaxPerProvider_NullCategory_ReturnsOverviewCapOfSix()
    {
        var method = typeof(SearchController).GetMethod(
            "ResolveMaxPerProvider",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (int)method!.Invoke(null, new object[] { null });

        result.Should().Be(6);
    }
}
