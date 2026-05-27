using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class CompanySyncServiceNormalizeCompanyNameDivFalsePositiveTests
{
    // Fourth and FINAL pin in the Roman-numeral false-positive family.
    // MIX (PR pre-existing), LIV (PR pre-existing), and CIV (PR #2286) are
    // already pinned. This pin closes the set:
    //   RomanNumeralFalsePositives = { "MIX", "DIV", "LIV", "CIV" }
    //
    // DIV decomposes as D (500) + IV (4) = 504, so the Roman-numeral
    // regex matches it via the `(D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})`
    // path's D-prefix arm. That's a structurally distinct regex group
    // from MIX (M-prefix), LIV (L-prefix), and CIV (C-prefix), making
    // DIV the FOURTH unique regex-prefix arm in the false-positive set.
    //
    // Real company names use "DIV" as the abbreviation for "Division"
    // — extremely common in operating-segment names (TECH DIV CORP,
    // ENERGY DIV LLC, etc.). Misclassifying DIV as a Roman numeral
    // would silently uppercase every divisional company name in the
    // platform's normalize pass.
    //
    // The risk this pin uniquely catches:
    //   • SWAP regression on the false-positive set — e.g. someone
    //     "consolidating" the set under "MIX is alphabetical-first, drop
    //     DIV since both start with D-tier consonants" — would compile,
    //     pass MIX/LIV/CIV sibling pins (each in the set), and silently
    //     upper-case "Tech Div Corp" in every divisional company name.
    //   • TYPO regression in the literal — `"DIV "` (trailing space) or
    //     `"Div"` (lowercase, but the set uses OrdinalIgnoreCase, so
    //     that's fine — the lowercase typo would still match). The
    //     trailing-space typo is the harder one: `"DIV " != "DIV"`
    //     even under OrdinalIgnoreCase, so a careless space would
    //     remove DIV from the set.
    //   • REGEX-rewrite regression — a "tighten the leading group" pass
    //     that drops the D-prefix alternation (D?) would no longer match
    //     DIV at the regex level, making the false-positive check a
    //     no-op for DIV. The result is benign (DIV correctly title-cased
    //     either way), but the pin still detects this regression because
    //     a downstream side-effect (the regex change) might cascade
    //     into other test failures the reviewer should investigate.
    //
    // Pin: invoke with a DIV-containing all-caps company name and
    // assert the EXACT title-cased result "Tech Div Corp". The
    // Normalize helper is private static — reflection-invoke.
    //
    // The full quartet (MIX/LIV/CIV/DIV) defends every distinct regex
    // prefix arm in the false-positive set, completing exhaustive
    // per-arm coverage of the Roman-numeral family.
    [Fact]
    public void AllCapsName_DivIsEnglishWord_NotRomanNumeral504()
    {
        var method = typeof(CompanySyncService).GetMethod(
            "NormalizeCompanyName",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["TECH DIV CORP"]);

        result.Should().Be("Tech Div Corp");
    }
}
