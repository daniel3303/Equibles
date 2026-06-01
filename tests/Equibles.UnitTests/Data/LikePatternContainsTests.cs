using Equibles.Data;

namespace Equibles.UnitTests.Data;

public class LikePatternContainsTests
{
    // Adversarial Lane A. LikePattern.Contains is the LIKE-injection guard behind
    // every typeahead search in the app (CommonStockRepository, InsiderOwnerRepository,
    // InstitutionalHolderRepository, CongressMemberRepository, FormAdvAdviserRepository,
    // ErrorRepository, InstitutionsController, …). Its contract: "escapes LIKE
    // metacharacters and wraps in % for a contains match." The load-bearing security
    // property is that a '%' the USER typed must come back escaped ("\%" — a literal
    // percent), while only the two wrapping '%' stay live wildcards. If escaping were
    // dropped or applied after the wrap, "50%" would yield "%50%%": the middle '%'
    // becomes a wildcard and the query matches every row — a typeahead that leaks the
    // whole table. The single assertion pins exactly that distinction.
    [Fact]
    public void Contains_InputWithUserPercent_EscapesUserPercentButKeepsWrappingWildcards()
    {
        var result = LikePattern.Contains("50%");

        result.Should().Be("%50\\%%");
    }
}
