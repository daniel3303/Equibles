using Equibles.Fred.Repositories.Search;

namespace Equibles.UnitTests.Search;

public class FredSeriesSearchProviderIdentityTests
{
    // Third in the SearchProvider identity-pin family (after
    // SecDocumentSearchProviderIdentityTests #2415 and
    // CommonStockSearchProviderIdentityTests #2416). The
    // FredSeriesSearchProvider is the Economic Indicators group of
    // the global search:
    //   public override string Category => "Economic Indicators";
    //   public override int Order => 20;
    //
    // The risks this pin uniquely catches and that are specific to
    // this arm of the provider family:
    //
    //   • Category rename — "Economic Indicators" → "FRED" /
    //     "Macro" / "EconomicSeries". The
    //     SearchCategoryRouteExtensions.CategoryUrl switch's
    //     "Economic Indicators" arm matches on the EXACT category
    //     string (pinned by SearchCategoryUrlEconomicIndicatorsArmTests
    //     #2383). A rename here would compile, pass the
    //     SecDocument/CommonStock identity siblings (different
    //     categories), and silently route the "See all Economic
    //     Indicators" link through the generic-search fallback —
    //     same regression class the #2383 CategoryUrl pin catches
    //     FROM THE CONSUMER side. This pin catches it FROM THE
    //     PROVIDER side; both pins together close the cross-
    //     component wiring contract.
    //
    //   • Category casing drift — `"Economic Indicators"` →
    //     `"economic indicators"` / `"Economic indicators"`. The
    //     CategoryUrl switch is a case-SENSITIVE string match
    //     (CategoryUrl uses `switch (category)` with no
    //     normalization). A casing drift would route the wrong way
    //     while LOOKING right in the rendered group header (which
    //     uses Category as the visible label).
    //
    //   • Order shift — `=> 20` → any other value. Order=20 places
    //     Economic Indicators third in the documented global-search
    //     ordering (Stocks=0, SEC Filings=10, Economic Indicators=20,
    //     Institutions=30, Insiders=40, Congress=50, Futures=60). A
    //     shift would re-rank the group's visual position; the
    //     "Economic Indicators" group would no longer appear
    //     immediately after SEC Filings as documented.
    //
    // Pin: instantiate with a null repository (both properties are
    // pure constants), assert both. Mirrors the verbatim shape of
    // SecDocumentSearchProviderIdentityTests and
    // CommonStockSearchProviderIdentityTests.
    [Fact]
    public void Category_AndOrder_ArePinnedConstants()
    {
        var sut = new FredSeriesSearchProvider(null);

        sut.Category.Should().Be("Economic Indicators");
        sut.Order.Should().Be(20);
    }
}
