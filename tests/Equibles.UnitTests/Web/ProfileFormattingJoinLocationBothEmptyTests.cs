using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class ProfileFormattingJoinLocationBothEmptyTests
{
    // Sibling to ProfileFormattingJoinLocationWhitespaceTests, which only
    // pins the *one-sided* fallback (`city, whitespace` → `city`). The
    // existing pin leaves the both-sides-empty case unpinned — which is
    // production-real on freshly-scraped institutional holders whose
    // EDGAR profile rows landed with neither City nor State populated
    // (a common shape for foreign filers and shell entities).
    //
    // Contract derived from the class doc ("Small presentation helpers
    // shared by the entity profile pages") and the method name
    // `JoinLocation` — the helper must NOT emit a stray ", " or a
    // leading-comma artifact when BOTH inputs are missing. The implementation
    // uses `IsNullOrWhiteSpace` on BOTH parts via the same Where filter; a
    // refactor that "harmonised" the predicate to filter only
    // `stateOrCountry` (under the assumption that the upstream guarantees
    // `city` is always present) would compile, pass the existing
    // one-sided sibling pin (`city = "San Francisco"`), and silently
    // emit `"  , "` for a both-empty row — visible on the profile page
    // as a stray-comma typo.
    //
    // Pin both parameters as whitespace-only. The assertion is the empty
    // string — anything else (null, ", ", " , ") indicates the filter
    // dropped or got asymmetric. This catches both the
    // IsNullOrWhiteSpace → IsNullOrEmpty regression AND the
    // "only-filter-stateOrCountry" regression in one assertion.
    [Fact]
    public void JoinLocation_BothCityAndStateOrCountryWhitespace_ReturnsEmptyString()
    {
        var result = ProfileFormatting.JoinLocation("  ", "\t");

        result.Should().Be(string.Empty);
    }
}
