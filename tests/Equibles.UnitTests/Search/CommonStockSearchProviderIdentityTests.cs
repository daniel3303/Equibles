using Equibles.CommonStocks.Repositories.Search;

namespace Equibles.UnitTests.Search;

public class CommonStockSearchProviderIdentityTests
{
    // Sibling to SecDocumentSearchProviderIdentityTests (#2415). The
    // CommonStockSearchProvider is the Stocks group of the global
    // search — Order=0 places it at the TOP of every search result
    // page. Both Category and Order are literal constants:
    //   public override string Category => "Stocks";
    //   public override int Order => 0;
    //
    // The risks this pin uniquely catches:
    //
    //   • Category rename — "Stocks" → "Stock" / "Tickers" /
    //     "Companies". The SearchCategoryRouteExtensions.CategoryUrl
    //     switch's "Stocks" arm matches on the exact category string;
    //     a rename would silently route the "See all Stocks" link
    //     through the generic-search fallback instead of
    //     /Stocks?search=<query>. The group header on the global-
    //     search results page also renders Category directly — a
    //     rename ships a visible UI regression.
    //
    //   • Order shift — `=> 0` → any non-zero. The aggregator orders
    //     groups by `Order` ascending; Stocks at 0 is the most
    //     important visual signal on every search (a user typing a
    //     ticker expects the stock to be the first hit). A shift to
    //     `=> 100` would bury Stocks beneath every other group
    //     (Filings 10, EconomicIndicators 20, Institutions 30,
    //     Insiders 40, Congress 50, Futures 60); operators searching
    //     for AAPL would scroll past 6 sibling groups before seeing
    //     the canonical AAPL hit.
    //
    //   • Constant-cast regression — `=> -1` (off-by-one from a
    //     "make it explicit non-zero" refactor): every other group's
    //     Order is positive, so a negative Stocks order silently
    //     keeps Stocks first but defeats the documented "0 = canonical
    //     primary" convention. The exact `=> 0` assertion catches
    //     this.
    //
    // Pin: instantiate the provider with a null repository (Category
    // and Order are pure constants — don't touch the repository) and
    // assert both. Mirrors SecDocumentSearchProviderIdentityTests
    // (#2415) shape verbatim.
    [Fact]
    public void Category_AndOrder_ArePinnedConstants()
    {
        var sut = new CommonStockSearchProvider(null);

        sut.Category.Should().Be("Stocks");
        sut.Order.Should().Be(0);
    }
}
