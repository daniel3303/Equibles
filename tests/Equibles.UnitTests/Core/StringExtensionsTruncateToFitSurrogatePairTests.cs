using Equibles.Core.Extensions;

namespace Equibles.UnitTests.Core;

public class StringExtensionsTruncateToFitSurrogatePairTests
{
    [Fact]
    public void TruncateToFit_CapLandsOnHighSurrogate_BacksOffToAvoidSplittingThePair()
    {
        // Contract: caps at maxLength UTF-16 units WITHOUT splitting a surrogate pair —
        // when the cap lands on the high half, it backs off one unit so the kept prefix
        // never ends in a lone surrogate. "😀" is the pair 😀 at indices 1-2.
        var result = "a😀b".TruncateToFit(2);

        result
            .Should()
            .Be(
                "a",
                "maxLength 2 lands on the emoji's high surrogate (index 1), so TruncateToFit "
                    + "must back off to length 1 rather than return an orphaned high surrogate"
            );
    }
}
