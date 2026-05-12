using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Integrations.Sec;

public class CompanyMetadataTests {
    [Fact]
    public void IsListed_OtcOnlyExchanges_ReturnsFalse() {
        // `CompanyMetadata.IsListed` powers the second-tier tiebreaker in
        // `CompanySyncService.ShouldIncumbentWin` — when two SEC filers race for the same
        // ticker, the LISTED company beats the unlisted one before falling through to the
        // CIK numerical tiebreak. The non-obvious part is that "OTC" alone does NOT count
        // as listed: subsidiaries that file with the SEC but trade only over-the-counter
        // are the strongest realistic source of ticker hijack risk (they share the public
        // ticker with their exchange-listed parent on the SEC submissions doc). The filter
        // is `Exchanges.Any(e => !IsNullOrWhiteSpace(e) && !Equals(e, "OTC", OrdinalIgnoreCase))`.
        //
        // The risk this test pins: a refactor that drops the `!Equals(e, "OTC", …)` clause
        // (or that swaps the comparer to case-sensitive Equals so "otc" / "Otc" slip past)
        // would silently mark every OTC-only subsidiary as listed. Each such filer would
        // then WIN ticker collisions against its parent — overwriting the real, exchange-
        // listed CommonStock with a subsidiary CIK that has no actual trading data. Pin
        // OTC-only → false with a lowercase variant in the list so the case-insensitive
        // comparison is also exercised.
        var metadata = new CompanyMetadata {
            Cik = "1234567",
            Exchanges = ["otc"],
        };

        metadata.IsListed.Should().BeFalse();
    }
}
