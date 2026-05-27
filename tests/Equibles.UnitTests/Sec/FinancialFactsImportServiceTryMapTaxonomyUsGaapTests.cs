using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapTaxonomyUsGaapTests
{
    // Final arm in the TryMapTaxonomy sweep. Dei, IfrsFull, Srt, Invest, and
    // NullKey-default are pinned by sibling files; this pin covers us-gaap —
    // the dominant taxonomy on every US filer's financial facts. After this
    // PR, 6/6 arms of the TryMapTaxonomy switch have individual per-arm pins.
    //
    // Why "us-gaap" is the highest-value of the remaining arms to pin:
    //   • US-GAAP is the FAA-mandated reporting standard for every US-
    //     listed issuer; well over 90% of FinancialFact rows in the
    //     SEC database carry this taxonomy key. Mapping it correctly is
    //     load-bearing for every revenue/earnings/equity query.
    //   • The us-gaap case is FIRST in the switch — structurally the
    //     most likely candidate for a "drop the top case" copy-paste
    //     pruning regression (e.g. someone removing what looks like a
    //     duplicate alongside a comment cleanup).
    //   • A swap regression — `"us-gaap" => FactTaxonomy.Dei` (the
    //     adjacent enum value) — would compile, pass every other
    //     sibling pin (each untouched), and silently REROUTE every
    //     US-GAAP fact into the Dei bucket. Dei is reserved for
    //     entity-cover-page metadata (filer name, EIN, period of
    //     report) — a flood of revenue/EPS rows tagged as Dei would
    //     pollute the cover-page surface and starve every financial-
    //     statement query.
    //   • A drop regression — collapsing us-gaap into default → returns
    //     false — would silently DROP every US-GAAP fact from import.
    //     The Financial Facts ingest would import ONLY non-US filers'
    //     IFRS data, plus the cover-page Dei rows. The 90%+ of facts
    //     that drive every dashboard would silently disappear.
    //
    // None of these regressions are reachable from the existing Dei,
    // IfrsFull, Srt, Invest, or Null-default sibling pins. Each switch
    // arm uses a distinct case label and a distinct return enum value,
    // so only an explicit us-gaap → UsGaap assertion closes the gap.
    //
    // Pin: invoke with "us-gaap" (canonical SEC wire form — lowercase,
    // matching the body's `.ToLowerInvariant()` normalization) and
    // assert BOTH the bool result is true AND the out parameter equals
    // exactly FactTaxonomy.UsGaap. Reflection-invoke since private
    // static. After this iteration, the TryMapTaxonomy switch has
    // exhaustive per-arm coverage — any single-arm corruption fails
    // on the corresponding sibling.
    [Fact]
    public void TryMapTaxonomy_UsGaapKey_ReturnsTrueWithUsGaapTaxonomy()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapTaxonomy",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "us-gaap", null };
        var result = (bool)method!.Invoke(null, args);

        result.Should().BeTrue();
        args[1].Should().Be(FactTaxonomy.UsGaap);
    }
}
