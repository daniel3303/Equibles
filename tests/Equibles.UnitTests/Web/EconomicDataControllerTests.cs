using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Tests for <see cref="EconomicDataController"/>. The public action methods hit
/// the EF DbContext via FredSeriesRepository/FredObservationRepository, so we
/// exercise the pure-logic private static helpers via reflection — same pattern
/// as CftcClientTests, CboeClientTests, and SecEdgarClientTests.
/// </summary>
public class EconomicDataControllerTests
{
    private static readonly MethodInfo ExpandFrequencyMethod =
        typeof(EconomicDataController).GetMethod(
            "ExpandFrequency",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void ExpandFrequency_BiweeklyCodeBw_ReturnsBiweekly()
    {
        // ExpandFrequency is the switch expression that converts FRED's
        // single/short-letter frequency codes ("D", "W", "BW", "M", "Q",
        // "SA", "A") into human-readable English labels for the
        // economic-data show page. The mapping is structurally a
        // switch expression with seven explicit arms plus a default
        // that returns the raw input. The labels surface on the
        // public site directly in the series detail page header
        // ("Frequency: Biweekly") and in the chart legend.
        //
        // Pin "BW" → "Biweekly" specifically because it's the ONLY
        // two-character code in the table — every other arm is a single
        // letter. The two-character match is the structurally fragile
        // arm:
        //   • A regression that adjusts the trim/upper pipeline (e.g.
        //     adds `.Substring(0, 1)` thinking "FRED frequencies are
        //     single letters") would compile, pass every single-letter
        //     pin, and silently bucket "BW" into the default arm,
        //     surfacing in the UI as "BW" instead of "Biweekly".
        //   • A regression that changes the switch arm order or fat-
        //     fingers `"BW" => "Weekly"` (adjacent codes, related
        //     domain) would silently mis-label biweekly series as
        //     weekly. FRED has ~200 biweekly series (most of the
        //     "30-day commercial paper rate" family — high-volume on
        //     fixed-income dashboards) and the misclassification would
        //     halve the apparent cadence in the chart x-axis.
        //
        // The case-normalization path (`?.Trim().ToUpperInvariant()`)
        // is exercised incidentally — input "BW" must match the
        // upper-case "BW" arm exactly. A regression that drops
        // `ToUpperInvariant()` (e.g. someone migrating to `switch`
        // statement that requires exact case) would compile, pass
        // tests that hand-craft uppercase input, but break on the
        // real FRED feed which routinely emits lowercase frequency
        // codes from older legacy series. Pinning the uppercase form
        // documents the contract.
        var result = (string)ExpandFrequencyMethod.Invoke(null, ["BW"]);

        result.Should().Be("Biweekly");
    }

    [Fact]
    public void ExpandFrequency_QuarterlyCodeQ_ReturnsQuarterly()
    {
        // Sibling to the existing `BW → Biweekly` pin. This pin
        // exercises a SINGLE-letter arm ("Q") — structurally distinct
        // from the two-character "BW" arm. The pair covers both
        // "shape" classes of the seven-arm switch.
        //
        // Pin "Q → Quarterly" specifically because of its business
        // weight: FRED's quarterly series carry the GDP family (real
        // GDP, GDP deflator, productivity), the federal-funds-rate
        // path's economic-summary report, and most of the BEA personal-
        // income aggregates. Those series anchor the
        // macro-dashboard page; misclassifying them as "Q" (the raw
        // default-arm fallback) would break the chart legend AND the
        // page header ("Frequency: Q" vs "Frequency: Quarterly").
        //
        // The risk uniquely caught by a single-letter pin (vs. the
        // existing BW two-character pin): a regression that swaps the
        // switch keys to ENUM values (a plausible "type-safety
        // improvement" refactor that introduces a Frequency enum and
        // breaks the string-key path entirely) would compile to a
        // compiler error before reaching the test only if EVERY caller
        // is updated. If the refactor leaves ExpandFrequency's string
        // signature intact but routes input through an enum-parse-then-
        // switch path, single-letter inputs that happen to match enum
        // names (Q, D, M, etc. ARE common enum-name initials for
        // Daily/Monthly/Quarterly) might silently fall through where
        // the textual BW does not. The two pins together — one
        // single-letter, one two-character — distinguish the cases.
        //
        // The complementary risk: arm-reordering or label-typo
        // regressions. Pin asserts the exact label "Quarterly" — a
        // refactor that typo'd to "Quaterly" (single-r), shortened to
        // "Qtrly", or accidentally pasted "Quarter" from a different
        // domain (e.g. an earnings-quarter label elsewhere) would all
        // fail this assertion. The BW pin couldn't catch any of these
        // because its label is two-syllable and structurally distinct.
        var result = (string)ExpandFrequencyMethod.Invoke(null, ["Q"]);

        result.Should().Be("Quarterly");
    }

    [Fact]
    public void ExpandFrequency_LowercaseInputViaToUpperInvariantNormalization_MatchesMonthlyArm()
    {
        // Fourth pin in the ExpandFrequency family. Existing pins cover BW
        // (two-character mapped arm), Q (single-letter mapped arm), and XYZ
        // (default catch-all). All three feed input ALREADY in upper-case form
        // — they happen to exercise the matched-arm logic but leave the case-
        // normalization pipeline `?.Trim().ToUpperInvariant()` untouched: an
        // input like "Q" matches the "Q" arm whether or not ToUpperInvariant
        // fires, because Q.ToUpperInvariant() == Q. This pin closes that
        // gap by feeding lowercase input that ONLY matches the mapped arm
        // through the case-normalization step.
        //
        // Production scenario: FRED routinely emits lowercase frequency codes
        // for older legacy series — the existing BW pin's commentary
        // explicitly calls this out ("real FRED feed which routinely emits
        // lowercase frequency codes from older legacy series") but never
        // exercises it. The historical-data backfill — CPI before ~2000, the
        // BEA personal-income aggregates from the 80s, monetary statistics
        // before the FRED2 migration — is the population most affected.
        //
        // Pin lowercase "m" specifically:
        //   • "M" → Monthly is the DOMINANT FRED cadence. CPI, payroll
        //     employment, retail sales, industrial production, housing
        //     starts, every BLS labor-market release, every PCE/PPI inflation
        //     series — all monthly. Drop the case-normalization and every
        //     legacy monthly series surfaces in the UI as "m" (literal
        //     input echoed through the default arm) instead of "Monthly".
        //   • "m" is single-character so it tests neither the `.Trim()`
        //     branch (no surrounding whitespace) nor the multi-character
        //     uppercase-each-character behavior (`ToUpperInvariant` on a
        //     single char is trivial). The pin exercises ONLY the
        //     case-normalization itself.
        //
        // The risk this pin uniquely catches and that's UNREACHABLE from any
        // existing sibling:
        //
        //   • A refactor that "tidies up" the switch selector to drop
        //     `.ToUpperInvariant()` — e.g. someone introducing a stricter
        //     `StringComparison.Ordinal`-style match under the (false)
        //     intuition that "FRED always returns canonical uppercase" —
        //     compiles cleanly, passes BW (uppercase), Q (uppercase), XYZ
        //     (default arm returns raw input regardless), AND silently
        //     mis-routes lowercase inputs into the default arm. The visible
        //     symptom: legacy monthly series render "m" in the page header
        //     instead of "Monthly".
        //
        //   • A refactor that swaps `?.Trim().ToUpperInvariant()` for
        //     `?.Trim().ToLower()` — the inverse of `ToUpperInvariant` — would
        //     also fail: the switch arms ARE uppercase, so a lowercased input
        //     wouldn't match. Existing tests fed uppercase strings that
        //     ToLower() turns lowercase, then no arm matches, then default
        //     fires and the BW/Q tests would fail FIRST (they expect mapped
        //     output, not raw echo). This pin reinforces from the other
        //     direction: lowercase IN → uppercase arm matches via
        //     normalization → mapped output.
        //
        //   • A refactor to a culture-sensitive `ToUpper()` (NOT
        //     `ToUpperInvariant`) is a Turkish-locale trap: on tr-TR,
        //     "i".ToUpper() == "İ" (dotted-I), not "I". FRED doesn't emit
        //     lowercase "i" as a frequency code so the regression
        //     wouldn't bite immediately, but pinning the
        //     "ToUpperInvariant"-specific behavior documents that the
        //     Invariant form is load-bearing.
        //
        // Construction: input lowercase "m", assert mapped output "Monthly".
        // Two-step proof: (a) the input did NOT match any arm directly
        // (the literal "m" isn't in the switch arms — all arms are
        // uppercase), and (b) the case-normalization step folded it
        // to "M" before the comparison fired. The single assertion on
        // the literal mapped string distinguishes both regression
        // classes (lost normalization, wrong-direction normalization).
        var result = (string)ExpandFrequencyMethod.Invoke(null, ["m"]);

        result.Should().Be("Monthly");
    }

    [Fact]
    public void ExpandFrequency_AnnualCodeA_ReturnsAnnualViaSingleLetterArm()
    {
        // Fifth pin in the ExpandFrequency family. Existing pins cover:
        //   • BW (two-character arm)
        //   • Q (single-letter arm — Quarterly)
        //   • Lowercase "m" (case-normalization via ToUpperInvariant)
        //   • Unknown code (default catch-all)
        // The single-letter "A" arm is the LAST mapped arm in the switch
        // expression and is still individually unpinned. The seven-arm
        // mapped table:
        //   "D" → Daily       (unpinned)
        //   "W" → Weekly      (unpinned)
        //   "BW" → Biweekly   (pinned)
        //   "M" → Monthly     (pinned via lowercase normalization)
        //   "Q" → Quarterly   (pinned)
        //   "SA" → Semiannual (unpinned)
        //   "A" → Annual      (THIS PIN)
        //
        // Why "A" specifically and why it's unreachable from every existing
        // sibling pin:
        //
        //   • The switch is exhaustive — each arm short-circuits on the
        //     first matching value. Existing arms (BW, Q, M) only fire on
        //     their own inputs; an input of "A" lands in NO existing pin
        //     directly. The default-arm pin (XYZ) doesn't exercise the "A"
        //     arm either — it lands in `_` because "XYZ" isn't in the
        //     allowlist.
        //
        //   • Production weight: FRED's annual series are the load-bearing
        //     macro indicators for long-horizon analyses — Census Bureau
        //     decennial-derived population estimates, BEA's national-accounts
        //     annual aggregates (GDP-by-state, personal-income-by-state,
        //     the historical NIPA tables that anchor every multi-year
        //     economic comparison). The annual cadence specifically reflects
        //     the statistical-agency publication cycle.
        //
        //   • A regression that drops the "A" arm specifically — most
        //     plausibly via a "consolidate adjacent semi/annual handling"
        //     refactor that merges "SA" and "A" into a single
        //     `Contains("annual")` lookup or collapses to a `"S?A"`-style
        //     pattern — would compile cleanly, pass BW/Q/m/XYZ pins, and
        //     silently surface raw "A" in the economy show-page header
        //     ("Frequency: A" vs "Frequency: Annual").
        //
        //   • A typo-class regression: `"A" => "Anual"` (single-n),
        //     `"A" => "Yearly"` (semantically equivalent but breaks the
        //     dashboard's exact-match label cache), or `"A" => "Annually"`
        //     (adverb vs noun). Each compiles cleanly and passes every
        //     existing pin — only this pin asserts the exact string
        //     "Annual".
        //
        // The single-letter "A" is also structurally the asymmetric
        // counterpart to the existing single-letter "Q" pin:
        //   • Q is the fifth mapped arm; A is the SEVENTH (last) arm. A
        //     regression that iterates the arms backwards or that collapses
        //     the tail of the alternation would break the A arm first
        //     while leaving Q untouched.
        //   • The single-letter pair (Q + A) covers both ends of the
        //     table.
        var result = (string)ExpandFrequencyMethod.Invoke(null, ["A"]);

        result.Should().Be("Annual");
    }

    [Fact]
    public void ExpandFrequency_UnknownCode_ReturnsRawInputUnchangedNotNormalized()
    {
        // Third pin in the ExpandFrequency family. Existing pins cover BW
        // (two-character) and Q (single-letter). This pin covers the
        // CATCH-ALL `_ => frequency` arm — the default that fires when
        // FRED emits a code outside the seven-arm whitelist.
        //
        // The default arm is structurally distinct in two ways from every
        // mapped arm:
        //
        // 1) Return shape: every mapped arm returns a STATIC LITERAL
        //    ("Daily", "Weekly", ...); the default returns the ORIGINAL
        //    `frequency` parameter (NOT the normalized
        //    `?.Trim().ToUpperInvariant()` form used for matching). That
        //    asymmetry is load-bearing — if a future FRED data set
        //    introduces, say, "10Y" or "M2" as a frequency code, the
        //    show page renders whatever the upstream emitted rather than
        //    losing the value to an empty string or "Unknown" sentinel.
        //    Operators viewing the page can read the literal upstream
        //    code and recognize "FRED added a new cadence we haven't
        //    mapped yet" — a self-documenting failure.
        //
        // 2) Whitespace and case preservation: the switch selector
        //    normalizes via `?.Trim().ToUpperInvariant()`, but the default
        //    arm returns the un-normalized input. A regression that
        //    "tidied up" the default to return the normalized form (e.g.
        //    `_ => frequency?.Trim().ToUpperInvariant() ?? frequency`)
        //    would compile, pass both existing pins (BW and Q are already
        //    in their canonical upper-trimmed form), and silently start
        //    stripping operator-visible diagnostic context — original-case
        //    "wEekLy" wire forms (suggesting an upstream parser bug) would
        //    surface as "WEEKLY" instead, masking the bug.
        //
        // The risk this pin uniquely catches: a refactor that adds an
        // "Unknown" sentinel return for the default arm — under the false
        // intuition that "unmapped codes shouldn't leak the raw input to
        // operators" — would compile, pass every mapped-arm pin, and break
        // the documented self-diagnostic contract. The literal upstream
        // code is exactly what an operator needs to file an issue against
        // FRED-mapping.
        //
        // Pin a non-whitespace, non-empty unknown code that's NOT in any
        // mapped arm. "XYZ" works — three uppercase letters, no overlap
        // with any FRED code. The pin asserts (a) the default arm fires
        // (no exception thrown, no null returned) AND (b) the input is
        // returned verbatim.
        var result = (string)ExpandFrequencyMethod.Invoke(null, ["XYZ"]);

        result.Should().Be("XYZ");
    }
}
