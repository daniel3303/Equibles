using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapFiscalPeriodQ1Tests
{
    // Family completion: TryMapFiscalPeriod has six switch arms — FY (pinned),
    // Q1/Q2/Q3/Q4 (Q4 pinned by PR #2272), default-Unknown (pinned). This pin
    // covers Q1, the earliest-emitted quarterly tag and the structurally
    // first-listed of the quarterly arms (the case most likely to be hit by
    // a "drop the top case" copy-paste pruning regression).
    //
    // Why Q1 is the right next pin after Q4 (high-value-first sequencing):
    //   • Q1 is the FIRST quarterly entry in calendar order — emitted on the
    //     10-Q that filers publish in late April / early May. It carries the
    //     year's opening revenue / EPS / net-income facts that every
    //     analyst dashboard uses as the trend baseline for the remaining
    //     three quarters.
    //   • A swap regression — `"Q1" => SecFiscalPeriod.Q2` (copy-paste from
    //     the line below) — would compile, pass the FY pin (different arm),
    //     pass the Q4 sibling pin (different arm), pass the Unknown pin
    //     (different arm), and silently MERGE every Q1 fact into the Q2
    //     bucket. The quarterly-comparison chart would show Q1 as flat,
    //     Q2 as double its real value, and analysts would see a Q1→Q2
    //     spike that doesn't exist.
    //   • A drop regression — collapsing Q1 into the default — would
    //     silently DROP every Q1-tagged fact from the import. Filers'
    //     first-quarter disclosures would simply not appear in the
    //     quarterly drilldowns.
    //
    // None of these regressions are reachable from the existing FY, Q4,
    // or Unknown sibling pins. Each switch arm uses a distinct case label
    // and a distinct return enum value, so only an explicit Q1 → Q1
    // assertion closes this gap.
    //
    // Pin: invoke with "Q1" (canonical SEC wire form) and assert BOTH
    // the bool result is true AND the out parameter equals exactly
    // SecFiscalPeriod.Q1. The dual assertion distinguishes the working
    // arm from a swap (different enum value) and from a collapse
    // (default returns false). Reflection-invoke since private static.
    //
    // The pair (Q1 + Q4 + FY + Unknown) now defends four of the six arms.
    // Q2 and Q3 remain as future iteration targets — together those will
    // give exhaustive per-arm coverage of the whole switch.
    [Fact]
    public void TryMapFiscalPeriod_Q1Token_ReturnsTrueWithQ1Out()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapFiscalPeriod",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "Q1", null };
        var result = (bool)method!.Invoke(null, args);

        result.Should().BeTrue();
        args[1].Should().Be(SecFiscalPeriod.Q1);
    }
}
