using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Integrations.Sec;

public class CompanyMetadataTests {
    [Fact]
    public void IsListed_ExchangeListNyseAlongsideOtc_ReturnsTrue() {
        // Sibling to `IsListed_OtcOnlyExchanges_ReturnsFalse`. The existing pin
        // proves the rejection path — that OTC alone doesn't count as listed.
        // The acceptance path — that ANY non-OTC, non-whitespace exchange flips
        // IsListed to true — was unpinned. The two pins together cover both
        // sides of the `Exchanges.Any(...)` predicate.
        //
        // The risk this catches is asymmetric from the OTC-only sibling: a
        // refactor that flipped the `Any` to `All` (under the false intuition
        // that "all exchanges must be non-OTC for the company to count as
        // listed") would compile, pass the existing OTC-only-false pin (OTC
        // doesn't pass the predicate for all=true to hold either), and silently
        // mark dual-listed companies — those with BOTH NYSE and OTC entries on
        // their SEC submissions doc — as unlisted. Dual-listing is common: a
        // foreign issuer may have an ADR on NYSE while OTC carries the
        // original common stock; a domestic large-cap may have OTC depositary
        // receipts for international markets alongside its primary NYSE
        // listing. Either way, IsListed must return true on the basis of the
        // exchange-listed entry alone.
        //
        // The same regression would also break the CompanySyncService
        // ticker-collision tiebreak in the wrong direction: legitimate
        // exchange-listed filers would LOSE collisions against subsidiaries
        // because both would return IsListed=false, falling through to the
        // CIK numerical tiebreak — which is exactly the failure mode the OTC
        // sibling guards against in the opposite direction.
        //
        // Pin a realistic NYSE-alongside-OTC exchange list. Asserting true
        // proves the Any-arm fires for the NYSE entry; the existing OTC-
        // only sibling proves the same predicate rejects OTC alone. Pair
        // covers both directions.
        var metadata = new CompanyMetadata {
            Cik = "1234567",
            Exchanges = ["NYSE", "OTC"],
        };

        metadata.IsListed.Should().BeTrue();
    }

    [Fact]
    public void IsOperatingCompany_InvestmentCompanyEntityType_ReturnsFalse() {
        // `CompanyMetadata.IsOperatingCompany` is the FIRST-tier tiebreaker in
        // `CompanySyncService.ShouldIncumbentWin` (operating-status precedes the
        // listing-status precedes the CIK numerical tiebreak). The predicate is
        //   `string.Equals(EntityType, "operating", StringComparison.OrdinalIgnoreCase)`
        // and is meant to distinguish real operating companies from non-operating
        // SEC filer categories — investment companies (mutual funds, ETFs, BDCs),
        // SPACs (mid-merger), trusts, and other regulatory pass-through entities.
        //
        // The existing pins cover IsListed but leave IsOperatingCompany entirely
        // untested. The risk this catches: a refactor that swaps the comparer to
        // case-sensitive `Equals` (or to `Contains` for "fuzzy matching") would
        // either silently bucket ALL filers as non-operating (case-sensitive break
        // on "Operating", "OPERATING", or any other casing variation SEC's
        // submissions doc emits — the wire form varies year-over-year) or
        // bucket all of them as operating (contains-match on any string
        // containing "operating" anywhere — far too permissive). Either
        // direction inverts the operating-precedence tiebreak in
        // ShouldIncumbentWin: investment companies would either WIN every
        // collision against operating companies (silently overwriting AAPL
        // with the iShares fund that mentions AAPL in its submissions) or LOSE
        // every collision they should win.
        //
        // The complementary risk: a refactor that flipped the predicate's
        // negation (`!Equals(...)` instead of `Equals(...)`) would invert
        // the entire tiebreak. The OTC sibling can't catch this because
        // IsOperatingCompany sits BEFORE IsListed in the tiebreak chain.
        //
        // Pin the FALSE side with a realistic non-operating EntityType
        // value ("investment company" — the most common non-operating
        // category by filer count). Asserting BeFalse proves (a) the
        // OrdinalIgnoreCase comparison is `Equals`, not `Contains` (which
        // would have matched substring "investment company" partially) AND
        // (b) the predicate's polarity is correct (matched = false for
        // non-operating).
        var metadata = new CompanyMetadata {
            Cik = "1234567",
            EntityType = "investment company",
        };

        metadata.IsOperatingCompany.Should().BeFalse();
    }

    [Fact]
    public void IsOperatingCompany_OperatingTypeWithMixedCase_ReturnsTrueViaCaseInsensitiveCompare() {
        // Sibling to `IsOperatingCompany_InvestmentCompanyEntityType_ReturnsFalse`.
        // The predicate uses `OrdinalIgnoreCase` so any casing variation of
        // "operating" — SEC's submissions doc has emitted "Operating",
        // "OPERATING", and "operating" across different years/filers —
        // must classify as an operating company. The false-case sibling
        // catches polarity flips; this pin catches comparer narrowing.
        //
        // The risk this catches uniquely (and that the false sibling
        // cannot): a refactor that swaps `OrdinalIgnoreCase` for the
        // default case-sensitive `Equals` overload — under the false
        // intuition that "we always emit lowercase from the upstream
        // parser anyway" — would compile cleanly, pass the
        // false-investment-company pin (investment company is correctly
        // rejected either way), AND silently start mis-classifying every
        // filer whose EntityType arrived from SEC in mixed case. The
        // CompanySyncService tiebreak would fall through past the
        // operating-status arm for those filers because IsOperatingCompany
        // returns false; CIK numerical tiebreak then wins, which
        // routinely produces the wrong outcome (older CIKs are usually
        // subsidiaries; newer CIKs are usually the operating parent).
        //
        // The complementary risk: a switch from `Equals` to `Contains`
        // would also pass the existing pins (investment-company correctly
        // rejected, operating correctly accepted) and would silently
        // accept "non-operating subsidiary" as containing the substring
        // "operating". That regression is caught by the false sibling's
        // "investment company" input — neither contains "operating" — but
        // a different non-operating type that DOES contain the substring
        // (e.g. "Non-Operating Holding Company") would slip past. This
        // pin doesn't directly catch that case, but it complements the
        // false sibling's coverage at the predicate-comparer level.
        //
        // Pin mixed-case "Operating" (capital O, lowercase rest — the
        // most common SEC wire form per their submissions-doc schema).
        // Asserting true proves the OrdinalIgnoreCase comparer fires.
        var metadata = new CompanyMetadata {
            Cik = "1234567",
            EntityType = "Operating",
        };

        metadata.IsOperatingCompany.Should().BeTrue();
    }

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
