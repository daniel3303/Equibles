using Equibles.Holdings.Repositories.Search;

namespace Equibles.UnitTests.Search;

public class InstitutionalHolderSearchProviderIdentityTests
{
    // Fifth in the SearchProvider identity-pin family (after #2415
    // SEC Filings, #2416 Stocks, #2417 Economic Indicators, #2418
    // Futures). The InstitutionalHolderSearchProvider is the
    // Institutions group of the global search:
    //   public override string Category => "Institutions";
    //   public override int Order => 30;
    //
    // The risks this pin uniquely catches:
    //
    //   • Category drift across the SearchHit.Kind boundary — the
    //     producer Category is "Institutions" (plural, group header)
    //     but Project() sets Kind = "Institution" (singular, used by
    //     HitUrl's Institution arm pinned by HitUrlInstitutionArmTests).
    //     A maintainer normalizing the two strings to match either
    //     direction would compile, pass the four earlier identity
    //     siblings (different categories), AND pass the HitUrl pin
    //     (also different category), but break either the routing
    //     contract (HitUrl arm matches on Kind="Institution") or the
    //     group-header rendering (Category="Institutions" is what the
    //     UI displays). The intentional singular/plural split must
    //     stay pinned at the producer.
    //
    //   • Order shift — `=> 30` → any other value. Order=30 places
    //     Institutions FOURTH in the documented global-search ordering
    //     (Stocks=0, SEC Filings=10, Economic Indicators=20,
    //     Institutions=30, Insiders=40, Congress=50, Futures=60).
    //     A shift would re-rank Institutions relative to Insiders and
    //     Congress — all three are profile-type entities and visually
    //     similar, so an off-by-ten shift is plausible-looking but
    //     wrong against the documented ordering contract.
    //
    // Pin: instantiate with a null repository (both properties are
    // pure constants), assert both. Mirrors the verbatim shape of
    // the four earlier identity siblings.
    [Fact]
    public void Category_AndOrder_ArePinnedConstants()
    {
        var sut = new InstitutionalHolderSearchProvider(null);

        sut.Category.Should().Be("Institutions");
        sut.Order.Should().Be(30);
    }
}
