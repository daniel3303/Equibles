using Equibles.Sec.Repositories.Search;

namespace Equibles.UnitTests.Search;

public class SecDocumentSearchProviderIdentityTests
{
    // SecDocumentSearchProvider is the SEC filings group in the global
    // search aggregator (#885). Zero existing test coverage.
    //
    // The Category and Order properties are the provider's identity
    // tokens that SearchAggregator reads to (a) label the group in the
    // UI and (b) sort groups across all providers. Both are literal
    // constants:
    //   public string Category => "SEC Filings";
    //   public int Order => 10;
    //
    // The risks this pin uniquely catches:
    //
    //   • Category rename — "SEC Filings" → "Filings" / "SEC" /
    //     "Documents" / "10-K Filings". The SearchCategoryRouteExtensions
    //     .CategoryUrl switch keys on the exact Category string for the
    //     "See all" link routing (see SearchCategoryUrlTests for the
    //     pattern); a rename would silently break the "See all SEC
    //     Filings" link from the global-search dropdown — every search
    //     result page would fall through to the generic search-page
    //     fallback instead of the dedicated filings browser. The
    //     SearchCategoryRouteMappingTests matrix uses Kind ("Filing"),
    //     not Category, so it can't detect this. Also: the user-facing
    //     group header on the search results page reads from Category
    //     directly — a rename ships a visible UI regression.
    //
    //   • Order shift — `=> 10` → `=> 0` / `=> 100`. The aggregator
    //     orders groups by `Order` ascending; shifting SEC Filings'
    //     order would re-shuffle its position in the result list. SEC
    //     Filings is intentionally placed at Order=10 (after Stocks
    //     at 0 by convention, before deeper detail groups); a shift
    //     would put SEC Filings either above Stocks (visually
    //     prioritising filings over the canonical ticker hit) or
    //     buried below FRED series / CFTC contracts.
    //
    // Pin: instantiate the provider with a null repository (Category
    // and Order are pure constants — don't touch the repository) and
    // assert both. A swap to a different category name or a different
    // order value surfaces here.
    [Fact]
    public void Category_AndOrder_ArePinnedConstants()
    {
        var sut = new SecDocumentSearchProvider(null);

        sut.Category.Should().Be("SEC Filings");
        sut.Order.Should().Be(10);
    }
}
