using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryBuildParsedFactFiscalYearFallbackTests
{
    // TryBuildParsedFact's three-step FiscalYear chain is
    // `resolved?.Year ?? value.Fy ?? value.End.Year`. The last fallback fires
    // when neither FiscalPeriodResolver (no FYE on the stock) nor the wire
    // payload supplies a year — a common shape for small / foreign filers
    // whose CommonStock row hasn't been seeded with FiscalYearEndMonth/Day yet
    // and whose Company Facts entry omits `fy`. Without the `?? value.End.Year`
    // tail, FiscalYear would land at 0 for every such fact (int default),
    // which silently corrupts both the (CommonStockId, FiscalYear,
    // FiscalPeriod) index lookup and any year-keyed downstream aggregate. A
    // refactor that dropped the final coalesce would compile, pass every test
    // with a populated Fy, and only surface as zero-year FinancialFact rows
    // in production.
    [Fact]
    public void TryBuildParsedFact_NoResolverAndNullWireFy_FiscalYearFallsBackToEndYear()
    {
        var serviceType = typeof(FinancialFactsImportService);
        var parsedFactType = serviceType.GetNestedType("ParsedFact", BindingFlags.NonPublic);

        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "FOO",
            FiscalYearEndMonth = null,
            FiscalYearEndDay = null,
        };
        var value = new CompanyFactValue
        {
            Start = null,
            End = new DateOnly(2024, 12, 31),
            Val = 100m,
            Accn = "0000320193-24-000123",
            Fy = null,
            Fp = "FY",
            Form = "10-K",
            Filed = new DateOnly(2025, 1, 15),
        };

        var method = serviceType.GetMethod(
            "TryBuildParsedFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var parsed = method.Invoke(
            null,
            [FactTaxonomy.UsGaap, "Assets", "Assets", "USD", value, stock]
        );

        parsed.Should().NotBeNull();
        var fiscalYear = (int)parsedFactType.GetProperty("FiscalYear").GetValue(parsed);
        fiscalYear.Should().Be(2024);
    }
}
