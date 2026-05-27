using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class EconomicDataControllerExpandFrequencyTrimWhitespaceTests
{
    // ExpandFrequency's per-arm sweep is now exhaustive (D/W/BW/M/Q/SA/A
    // plus default plus case-normalisation pins). What no existing pin
    // exercises is the OTHER normalisation in the pipeline: the
    // `.Trim()` step that runs BEFORE `.ToUpperInvariant()`:
    //     frequency?.Trim().ToUpperInvariant() switch { ... }
    //
    // FRED API responses occasionally carry whitespace around frequency
    // codes (the agency's own data exports have historical formatting
    // inconsistencies, especially on the v2/categories endpoint where
    // series-summary rows are TAB-padded). Without the Trim, a
    // response with " D " would route every Treasury-yield daily
    // series to the default arm and the dashboard would render
    // " D " in the frequency chip instead of "Daily".
    //
    // The risk this pin uniquely catches:
    //   • Dropped Trim — `frequency?.ToUpperInvariant()` (someone
    //     "simplifies" the chain assuming FRED inputs are clean) —
    //     would compile, pass every per-arm pin (each feeds clean
    //     input), pass the case-normalisation pin (its input "m"
    //     has no surrounding whitespace), and silently route every
    //     whitespace-bearing FRED response to the default arm.
    //   • Reordered Trim — `frequency?.ToUpperInvariant().Trim()` —
    //     functionally equivalent for ASCII codes (whitespace
    //     survives ToUpperInvariant) so this regression is undetectable
    //     even by this pin; that's a known limitation, not a target.
    //   • Wrong Trim — `frequency?.TrimEnd()` (someone "narrows" the
    //     trim under "FRED only pads on the right") — would pass
    //     trailing-space inputs but fail leading-space inputs. The
    //     SYMMETRIC two-sided " D " input catches this.
    //
    // The pin's adversarial pair (leading AND trailing space)
    // distinguishes: a TrimEnd-only or TrimStart-only narrowing
    // regression — both would fail because one side stays untrimmed
    // and the switch sees " D" or "D " (neither matches).
    [Fact]
    public void ExpandFrequency_CodeWithSurroundingWhitespace_TrimsBeforeMatching()
    {
        var method = typeof(EconomicDataController).GetMethod(
            "ExpandFrequency",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [" D "]);

        result.Should().Be("Daily");
    }
}
