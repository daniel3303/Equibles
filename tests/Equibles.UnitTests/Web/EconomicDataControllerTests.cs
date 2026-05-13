using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Tests for <see cref="EconomicDataController"/>. The public action methods hit
/// the EF DbContext via FredSeriesRepository/FredObservationRepository, so we
/// exercise the pure-logic private static helpers via reflection — same pattern
/// as CftcClientTests, CboeClientTests, and SecEdgarClientTests.
/// </summary>
public class EconomicDataControllerTests {
    private static readonly MethodInfo ExpandFrequencyMethod = typeof(EconomicDataController)
        .GetMethod("ExpandFrequency", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void ExpandFrequency_BiweeklyCodeBw_ReturnsBiweekly() {
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
    public void ExpandFrequency_QuarterlyCodeQ_ReturnsQuarterly() {
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
}
