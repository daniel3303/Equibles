using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryBuildParsedFactUnmappableFpTests
{
    // Sibling to FinancialFactsImportServiceTryBuildParsedFactWhitespaceAccnTests
    // (whitespace-Accn rejection). This pin covers the structurally distinct
    // FIRST early-return arm of TryBuildParsedFact:
    //   if (!TryMapFiscalPeriod(value.Fp, out var fiscalPeriod)) return null;
    //
    // TryMapFiscalPeriod is itself exhaustively pinned per-arm (FY, Q1, Q2,
    // Q3, Q4, Unknown — PRs #2272-#2275). This sibling pin defends the
    // CALLER's reject-on-unmappable behavior: when TryMapFiscalPeriod
    // returns false, TryBuildParsedFact must return null so the fact is
    // dropped from the import set rather than fabricated with a default
    // SecFiscalPeriod value.
    //
    // SEC's Company Facts API occasionally serves placeholder/future fp
    // values: pre-publish "CQ1" (calendar Q1, distinct from fiscal Q1)
    // values in pre-release filings, or future schema variants. The
    // default(SecFiscalPeriod) for `fiscalPeriod` is FullYear (the
    // zero-valued enum member). Without the reject — i.e. if a refactor
    // dropped the early-return — every unmappable fp would silently
    // default to FullYear and corrupt the dashboards by routing every
    // unrecognized-period fact into the annual bucket.
    //
    // The risk this pin uniquely catches and that the whitespace-Accn
    // sibling cannot:
    //   • DROP-the-fp-early-return — `if (!TryMapFiscalPeriod(...)) ;`
    //     (drop the return null body) — would compile, pass the
    //     whitespace-Accn sibling (its Fp is "FY", valid), and silently
    //     create a ParsedFact with FiscalPeriod=FullYear for every
    //     unmappable wire input. CollapseToNaturalKey would then merge
    //     it with the actual FullYear fact for the same period and
    //     keep whichever has the later FiledDate — corrupting the
    //     restated-value tracking.
    //   • Wrong-arm-order regression — checking Accn before Fp would
    //     change the production order of rejection but not the result
    //     for this test (both are reject paths). Benign.
    //   • SWAP-to-throw — `return null` → `throw new
    //     ArgumentException(...)` — would compile, pass the whitespace
    //     sibling (its Fp is "FY", success arm fires before the throw
    //     could trigger), and crash on every legacy-format fp value.
    //
    // Pin: build a CompanyFactValue with Fp="FOOBAR" (a fully unrecognised
    // wire token that exercises only the unmappable-Fp branch). Assert
    // the helper returns null. Reflection-invoke since private static.
    //
    // After this PR, the two reject paths of TryBuildParsedFact (unmappable
    // Fp + whitespace Accn) are individually defended.
    [Fact]
    public void TryBuildParsedFact_UnmappableFpToken_ReturnsNull()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryBuildParsedFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var value = new CompanyFactValue
        {
            Fp = "FOOBAR",
            Accn = "0000320193-24-000001",
            End = new DateOnly(2024, 12, 31),
            Val = 100m,
            Fy = 2024,
            Filed = new DateOnly(2025, 1, 15),
        };
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            FiscalYearEndMonth = 9,
            FiscalYearEndDay = 30,
        };

        var result = method!.Invoke(
            null,
            [FactTaxonomy.UsGaap, "Revenues", "Revenues", "USD", value, stock]
        );

        result.Should().BeNull();
    }
}
