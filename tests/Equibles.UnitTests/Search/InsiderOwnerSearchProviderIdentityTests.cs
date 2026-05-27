using Equibles.InsiderTrading.Repositories.Search;

namespace Equibles.UnitTests.Search;

public class InsiderOwnerSearchProviderIdentityTests
{
    // Sixth in the SearchProvider identity-pin family (after #2415
    // SEC Filings, #2416 Stocks, #2417 Economic Indicators, #2418
    // Futures, #2419 Institutions). The InsiderOwnerSearchProvider
    // is the Insiders group of the global search:
    //   public override string Category => "Insiders";
    //   public override int Order => 40;
    //
    // The risks this pin uniquely catches:
    //
    //   • Category drift across the SearchHit.Kind boundary — the
    //     producer Category is "Insiders" (plural, group header) but
    //     Project() sets Kind = "Insider" (singular, used by HitUrl's
    //     Insider arm pinned by HitUrlInsiderArmTests). Same shape as
    //     the Institutions sibling (#2419) — a maintainer normalizing
    //     the two strings to match would compile and silently break
    //     either routing (HitUrl arm matches on Kind="Insider") or
    //     the group-header rendering (Category="Insiders" is what the
    //     UI displays). The intentional singular/plural split must
    //     stay pinned at the producer.
    //
    //   • Order shift — `=> 40` → any other value. Order=40 places
    //     Insiders FIFTH in the documented global-search ordering
    //     (Stocks=0, SEC Filings=10, Economic Indicators=20,
    //     Institutions=30, Insiders=40, Congress=50, Futures=60).
    //     Insiders sits BETWEEN Institutions and Congress — three
    //     people-profile groups in a row. An off-by-ten swap with
    //     either neighbor would compile and pass every other pin in
    //     the family.
    //
    // Pin: instantiate with a null repository (both properties are
    // pure constants), assert both. Mirrors the verbatim shape of
    // the five earlier identity siblings.
    [Fact]
    public void Category_AndOrder_ArePinnedConstants()
    {
        var sut = new InsiderOwnerSearchProvider(null);

        sut.Category.Should().Be("Insiders");
        sut.Order.Should().Be(40);
    }
}
