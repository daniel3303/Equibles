using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

// Pins TryBuildParsedFact's handling of an unmappable fp token. SEC serves
// foreign private issuers' 6-K interim values with fp = null (and pre-publish
// filings occasionally carry placeholder tokens like "CQ1"), so an unmappable
// fp must NOT drop the value outright — that left every FPI's interim facts
// missing platform-wide. The date-derived identity (FiscalPeriodResolver) is
// the primary source; the value is kept whenever the dates classify.
//
// The corruption the old reject guarded against is still guarded: when the
// dates DON'T classify either (unknown FYE, unrecognised span), the value is
// dropped rather than defaulting FiscalPeriod to the zero-valued enum member
// (FullYear), which would route every unplaceable fact into the annual bucket.
public class FinancialFactsImportServiceTryBuildParsedFactUnmappableFpTests
{
    private static object Invoke(CompanyFactValue value, CommonStock stock)
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryBuildParsedFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        return method!.Invoke(
            null,
            [FactTaxonomy.UsGaap, "Revenues", "Revenues", null, "USD", value, stock]
        );
    }

    private static CompanyFactValue UnmappableFpInstant() =>
        new()
        {
            Fp = "FOOBAR",
            Accn = "0000320193-24-000001",
            End = new DateOnly(2024, 12, 31),
            Val = 100m,
            Fy = 2024,
            Filed = new DateOnly(2025, 1, 15),
        };

    [Fact]
    public void TryBuildParsedFact_UnmappableFpWithResolvableDates_KeepsDateDerivedIdentity()
    {
        // Instant at Dec 31 against a Sep-30 FYE: first quarter of FY2025.
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            FiscalYearEndMonth = 9,
            FiscalYearEndDay = 30,
        };

        var result = Invoke(UnmappableFpInstant(), stock);

        result.Should().NotBeNull("the date-derived identity places the period without an fp");
        var type = result.GetType();
        type.GetProperty("FiscalYear")!.GetValue(result).Should().Be(2025);
        type.GetProperty("FiscalPeriod")!.GetValue(result).Should().Be(SecFiscalPeriod.Q1);
    }

    [Fact]
    public void TryBuildParsedFact_UnmappableFpAndUnknownFye_ReturnsNull()
    {
        // No fiscal-year end on the stock → the resolver cannot place the period
        // either; defaulting would corrupt the annual bucket, so the value drops.
        var stock = new CommonStock { Ticker = "AAPL" };

        var result = Invoke(UnmappableFpInstant(), stock);

        result.Should().BeNull();
    }
}
