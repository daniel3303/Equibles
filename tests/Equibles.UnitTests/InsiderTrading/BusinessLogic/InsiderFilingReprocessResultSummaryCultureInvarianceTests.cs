using System.Globalization;
using Equibles.InsiderTrading.BusinessLogic.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingReprocessResultSummaryCultureInvarianceTests
{
    // Adversarial Lane A. InsiderFilingReprocessResult.Summary is an
    // operator-facing log helper: it is consumed only by
    //   Logger.LogInformation("Insider filing reprocess cycle: {Summary}", result.Summary)
    // in InsiderFilingReprocessWorker.
    //
    // The repo's established convention for log/render helpers is
    // CultureInfo.InvariantCulture (cf. BaseScraperWorker.FormatInterval's
    // culture-invariance pins and FactMarkdown.Value, which thread invariant
    // through every ToString). Summary's body, however, uses plain
    // interpolated `:N0` segments:
    //     $"Reprocessed {Processed:N0}/{Total:N0} filings ..."
    // which, per the C# spec, format with formatProvider=null - defaulting to
    // thread CurrentCulture.
    //
    // The bug class (same one tracked across the repo's other culture pins):
    // under a non-invariant CurrentCulture with a dot group separator (de-DE),
    // a count of 1234 renders as "1.234" instead of the invariant "1,234".
    // Production impact: log lines fork by host locale - "1.234 fetched" on a
    // de-DE host vs "1,234 fetched" elsewhere - breaking grep/awk operator
    // triage and any log-parsing pipeline that keys on the group separator.
    //
    // The contract / oracle (derived from the repo convention before reading
    // the body): Summary renders its counts culture-invariantly - the group
    // separator is always ',' regardless of host culture. de-DE is the
    // canonical non-invariant culture used by the repo's other culture pins.
    [Fact]
    public void Summary_LargeCountsUnderNonInvariantCulture_RendersWithInvariantGroupSeparator()
    {
        var result = new InsiderFilingReprocessResult
        {
            Processed = 1234,
            Total = 5678,
            Fetched = 9012,
            Reclassified = 3456,
            Repaired = 7890,
            Failed = 2345,
        };

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            result
                .Summary.Should()
                .Be(
                    "Reprocessed 1,234/5,678 filings (9,012 fetched, 3,456 rows reclassified, "
                        + "7,890 prices repaired, 2,345 failed).",
                    "log helpers in this repo are culture-invariant (cf. BaseScraperWorker.FormatInterval, "
                        + "FactMarkdown.Value); a non-invariant group separator forks operator log output by host locale"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
