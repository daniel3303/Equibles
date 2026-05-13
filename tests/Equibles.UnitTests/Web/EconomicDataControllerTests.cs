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
}
