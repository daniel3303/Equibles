using System.Globalization;
using System.Reflection;
using Equibles.Worker;

namespace Equibles.UnitTests.Worker;

public class BaseScraperWorkerFormatIntervalCultureInvarianceTests
{
    // Adversarial Lane A. The three existing FormatInterval sibling pins
    // (Hours/Minutes/Seconds) all use clean integer values (2h, 5m, etc.)
    // where the `0.#` format spec emits no decimal separator at all -
    // so culture-sensitivity is invisible there.
    //
    // FormatInterval is a log-formatting helper: the operator-facing
    // "next run in X" string. The repo's established convention for log
    // formatters is CultureInfo.InvariantCulture (see FactMarkdown.Value
    // which explicitly threads it through every ToString). FormatInterval's
    // body, however, uses a plain interpolated string:
    //     return $"{interval.TotalHours:0.#}h";
    // which, per the C# spec, calls `((IFormattable)x).ToString(format,
    // formatProvider)` with formatProvider=null - defaulting to thread
    // CurrentCulture.
    //
    // The bug class: under a non-invariant CurrentCulture (de-DE, fr-FR,
    // pt-PT, any culture with a comma decimal), a 1.5-hour sleep renders
    // as "1,5h" instead of "1.5h". Production impact:
    //   • Log parsing pipelines that split on decimal point break.
    //   • grep/awk operator triage against the established convention
    //     misses the line.
    //   • A worker logs "Sleeping for 1,5h" alongside other workers
    //     logging "Sleeping for 2h" - inconsistent within the same
    //     incident.
    //
    // The contract: log helpers in this repo are culture-invariant. The
    // oracle (derived before reading the method body): a 1.5-hour
    // interval renders as "1.5h" under EVERY culture.
    //
    // Test strategy: capture, switch to de-DE (comma decimal), invoke,
    // restore. de-DE is the canonical "non-invariant comma decimal"
    // culture used across the repo's other culture pins (FormBindingFixture
    // names this explicitly). The result MUST be "1.5h" with a literal
    // dot - never "1,5h".
    [Fact]
    public void FormatInterval_FractionalHoursUnderNonInvariantCulture_RendersWithInvariantDecimalPoint()
    {
        var method = typeof(BaseScraperWorker).GetMethod(
            "FormatInterval",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = (string)method!.Invoke(null, [TimeSpan.FromMinutes(90)]);

            result
                .Should()
                .Be(
                    "1.5h",
                    "log formatters in this repo are culture-invariant (cf. FactMarkdown.Value); a non-invariant decimal separator forks operator log output by host locale"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // The minutes branch has its own ToString site
    // (`interval.TotalMinutes.ToString("0.#", ...)`), so the
    // culture-invariance contract needs a separate pin from the hours
    // branch. 90 seconds renders via the minutes arm (TotalHours < 1,
    // TotalMinutes = 1.5) — same fractional-decimal risk surface.
    // Contract: fractional minutes render culture-invariantly as
    // "1.5m" under every culture, never "1,5m".
    [Fact]
    public void FormatInterval_FractionalMinutesUnderNonInvariantCulture_RendersWithInvariantDecimalPoint()
    {
        var method = typeof(BaseScraperWorker).GetMethod(
            "FormatInterval",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = (string)method!.Invoke(null, [TimeSpan.FromSeconds(90)]);

            result
                .Should()
                .Be(
                    "1.5m",
                    "the minutes branch of FormatInterval must render culture-invariantly for the same reason as the hours branch (GH-2426); a non-invariant decimal separator forks operator log output by host locale"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
