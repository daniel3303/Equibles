using Equibles.Congress.Repositories.Search;

namespace Equibles.UnitTests.Search;

public class CongressMemberSearchProviderIdentityTests
{
    // Seventh (and final unpinned) in the SearchProvider identity-pin
    // family (after #2415 SEC Filings, #2416 Stocks, #2417 Economic
    // Indicators, #2418 Futures, #2419 Institutions, #2420 Insiders).
    // The CongressMemberSearchProvider is the Congress group of the
    // global search:
    //   public override string Category => "Congress";
    //   public override int Order => 50;
    //
    // The risks this pin uniquely catches:
    //
    //   • Category rename — "Congress" → "Congress Members" /
    //     "Politicians" / "Lawmakers" / "Representatives". Unlike the
    //     Institutions and Insiders siblings, this provider has NO
    //     plural/singular split (Category="Congress", Kind=
    //     "CongressMember") — the doc-comment intentionally chose
    //     "Congress group" as a topical category rather than mirroring
    //     the entity name. A rename to anything more "consistent" with
    //     siblings would break the documented contract this pin
    //     captures.
    //
    //   • Order shift — `=> 50` → any other value. Order=50 places
    //     Congress SIXTH in the documented global-search ordering
    //     (Stocks=0, SEC Filings=10, Economic Indicators=20,
    //     Institutions=30, Insiders=40, Congress=50, Futures=60).
    //     Congress is the last people-profile group before Futures;
    //     an off-by-ten swap with Insiders or Futures would compile.
    //
    // Pin: instantiate with a null repository (both properties are
    // pure constants), assert both. Mirrors the verbatim shape of
    // the six earlier identity siblings — and completes the family.
    [Fact]
    public void Category_AndOrder_ArePinnedConstants()
    {
        var sut = new CongressMemberSearchProvider(null);

        sut.Category.Should().Be("Congress");
        sut.Order.Should().Be(50);
    }
}
