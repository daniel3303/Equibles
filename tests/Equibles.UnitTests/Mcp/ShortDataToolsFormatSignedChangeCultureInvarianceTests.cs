using System.Globalization;
using System.Reflection;
using Equibles.Finra.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class ShortDataToolsFormatSignedChangeCultureInvarianceTests
{
    // Adversarial Lane A. ShortDataTools.FormatSignedChange is unpinned
    // and its body uses interpolated `N0` without InvariantCulture:
    //     change >= 0 ? $"+{change:N0}" : change.ToString("N0");
    //
    // Per the repo's established log/output-formatter convention (see
    // FactMarkdown.Value which threads InvariantCulture through every
    // ToString — and the prior #2426 finding on
    // BaseScraperWorker.FormatInterval), N0 without an explicit
    // IFormatProvider defaults to thread CurrentCulture's NumberFormat.
    // de-DE uses '.' as thousand separator, fr-FR uses NBSP — so
    // 1,234,567 renders as "1.234.567" / "1 234 567" under those
    // cultures. The output is consumed by MCP LLM clients trained on
    // en-US conventions; a host-locale-dependent thousand separator
    // breaks LLM parsing and forks operator log output by deploy
    // environment.
    //
    // The contract (derived from convention + the repo's other log
    // formatters): FormatSignedChange MUST render culture-invariantly
    // with US-style comma thousand separators, regardless of
    // thread CurrentCulture.
    //
    // Test strategy mirrors the BaseScraperWorker culture-invariance
    // sibling: capture original culture, switch to de-DE (canonical
    // non-invariant comma-decimal/dot-thousand locale used across
    // the repo's other culture pins), reflection-invoke the private
    // static, restore in finally. Expected: "+1,234,567" with
    // ASCII commas. Failure manifests as the comma-vs-dot
    // separator mismatch.
    [Fact(
        Skip = "GH-2444 — FormatSignedChange renders N0 thousand separator with host CurrentCulture instead of invariant"
    )]
    public void FormatSignedChange_PositiveValueUnderNonInvariantCulture_RendersWithInvariantCommaThousandSeparator()
    {
        var method = typeof(ShortDataTools).GetMethod(
            "FormatSignedChange",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = (string)method!.Invoke(null, [1_234_567L]);

            result
                .Should()
                .Be(
                    "+1,234,567",
                    "log/MCP output formatters in this repo are culture-invariant (cf. FactMarkdown.Value); a non-invariant thousand separator forks LLM-consumed output by host locale"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
