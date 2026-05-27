using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class CompanySyncServiceNormalizeCompanyNameCivFalsePositiveTests
{
    // Third pin in the Roman-numeral false-positive family (MIX and LIV
    // already pinned; DIV remains as a future target). The
    // RomanNumeralFalsePositives set is `{ "MIX", "DIV", "LIV", "CIV" }`
    // — every entry that decomposes as a valid Roman numeral but is
    // also a common English/abbreviation token in a company-name context.
    //
    // Why CIV specifically: it decomposes as C (100) + IV (4) = 104, so
    // the Roman-numeral regex matches it. The C-prefixed false positive
    // exercises a DIFFERENT regex group from MIX (M-prefix) and LIV
    // (L-prefix) — it's the "leading C with descending IV" path
    // through `(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})`. Real
    // company names use "CIV" as the abbreviation for "Civil" (e.g.
    // CIV CONSTRUCTION CORP, CIV ENGINEERING ASSOCIATES) or "Civic" —
    // not as the Roman numeral 104.
    //
    // The risk this pin uniquely catches:
    //   • A "consolidate the false-positive set" refactor that drops
    //     CIV under the (false) intuition that the C-prefix decomposition
    //     is rare in business names — would compile, pass MIX (M-prefix)
    //     and LIV (L-prefix) sibling pins, and silently upper-case
    //     "CIV" in every company name. "Civ Engineering" would render
    //     as "CIV ENGINEERING" in dashboards and search results.
    //   • A regex-rewrite refactor that tightened the leading group
    //     (e.g. dropping the C{0,3} alternation under "we don't see
    //     C-leading numerals in real names") — would no longer match
    //     CIV at all, removing it from the false-positive path entirely
    //     and again uppercasing CIV → no, wait — if the regex doesn't
    //     match CIV, then `IsRomanNumeral` returns false (regex match
    //     is the first conjunct), so CIV would correctly title-case.
    //     This case is benign. The pin still catches it because the
    //     assertion target is "Civ" — either the regex-doesn't-match
    //     OR the false-positive-set-contains arm correctly title-cases.
    //   • The asymmetric case that matters: regex matches AND
    //     false-positive set is corrupted (someone replaces CIV with
    //     a typo like "CIV " or "civ"). The set lookup uses
    //     OrdinalIgnoreCase, but a typo with trailing space would
    //     drop CIV from the set, causing CIV to stay uppercase.
    //
    // Pin: invoke with a CIV-containing all-caps company name and
    // assert the EXACT title-cased result. The Normalize helper is
    // a private static — reflection-invoke. Pair (MIX + LIV + CIV)
    // covers three of the four false positives at three structurally
    // distinct regex groups.
    [Fact]
    public void AllCapsName_CivIsEnglishWord_NotRomanNumeral104()
    {
        var method = typeof(CompanySyncService).GetMethod(
            "NormalizeCompanyName",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["CIV ENGINEERING CORP"]);

        result.Should().Be("Civ Engineering Corp");
    }
}
