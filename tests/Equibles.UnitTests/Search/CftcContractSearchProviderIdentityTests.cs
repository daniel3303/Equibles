using Equibles.Cftc.Repositories.Search;

namespace Equibles.UnitTests.Search;

public class CftcContractSearchProviderIdentityTests
{
    // Fourth in the SearchProvider identity-pin family (after #2415
    // SEC Filings, #2416 Stocks, #2417 Economic Indicators). The
    // CftcContractSearchProvider is the Futures group of the global
    // search:
    //   public override string Category => "Futures";
    //   public override int Order => 60;
    //
    // The risks this pin uniquely catches:
    //
    //   • Category rename — "Futures" → "CFTC" / "Commodities" /
    //     "Futures Markets". The SearchCategoryRouteExtensions
    //     .CategoryUrl switch's "Futures" arm (pinned by
    //     SearchCategoryUrlFuturesArmTests) matches on the EXACT
    //     category string. A rename here would compile, pass the
    //     three earlier identity siblings (different categories), and
    //     silently route the "See all Futures" link through the
    //     generic-search fallback instead of /Cftc.
    //
    //   • Order shift — `=> 60` → any other value. Order=60 places
    //     Futures LAST in the documented global-search ordering
    //     (Stocks=0, SEC Filings=10, Economic Indicators=20,
    //     Institutions=30, Insiders=40, Congress=50, Futures=60).
    //     A shift would re-rank Futures forward — CFTC contracts
    //     are intentionally placed last because they're a niche
    //     group; surfacing them above ticker hits would push the
    //     dominant Stocks/Filings groups down.
    //
    // Pin: instantiate with a null repository (both properties are
    // pure constants), assert both. Mirrors the verbatim shape of
    // the three earlier identity siblings.
    [Fact]
    public void Category_AndOrder_ArePinnedConstants()
    {
        var sut = new CftcContractSearchProvider(null);

        sut.Category.Should().Be("Futures");
        sut.Order.Should().Be(60);
    }
}
