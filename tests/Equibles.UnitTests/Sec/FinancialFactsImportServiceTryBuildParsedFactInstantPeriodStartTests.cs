using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryBuildParsedFactInstantPeriodStartTests
{
    // Load-bearing contract spelled out on FinancialFact.PeriodStart: for
    // instant facts (balance-sheet line items — Start = null on the wire), the
    // parser must set PeriodStart EQUAL to PeriodEnd so the unique index
    // (CommonStockId, FinancialConceptId, Unit, PeriodStart, PeriodEnd,
    // AccessionNumber) stays NULL-free — Postgres treats NULLs as distinct in
    // unique indexes, so a NULL PeriodStart would silently let restatements
    // re-insert duplicate rows instead of upserting in place. A refactor
    // dropping the `value.Start ?? value.End` fallback to just `value.Start`
    // would compile, pass any duration-fact test, and break the entire
    // restatement-collapse invariant on the next instant fact imported.
    [Fact]
    public void TryBuildParsedFact_InstantFact_PeriodStartEqualsPeriodEndAndPeriodTypeIsInstant()
    {
        var serviceType = typeof(FinancialFactsImportService);
        var parsedFactType = serviceType.GetNestedType("ParsedFact", BindingFlags.NonPublic);

        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var instantEnd = new DateOnly(2024, 12, 31);
        var value = new CompanyFactValue
        {
            Start = null,
            End = instantEnd,
            Val = 100m,
            Accn = "0000320193-24-000123",
            Fy = 2024,
            Fp = "FY",
            Form = "10-K",
            Filed = new DateOnly(2024, 11, 1),
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
        var periodType = (FactPeriodType)parsedFactType.GetProperty("PeriodType").GetValue(parsed);
        var periodStart = (DateOnly)parsedFactType.GetProperty("PeriodStart").GetValue(parsed);
        var periodEnd = (DateOnly)parsedFactType.GetProperty("PeriodEnd").GetValue(parsed);

        periodType.Should().Be(FactPeriodType.Instant);
        periodStart.Should().Be(instantEnd);
        periodEnd.Should().Be(instantEnd);
    }
}
