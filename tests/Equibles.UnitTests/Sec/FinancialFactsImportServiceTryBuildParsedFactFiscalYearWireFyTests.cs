using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryBuildParsedFactFiscalYearWireFyTests
{
    [Fact]
    public void TryBuildParsedFact_NoResolverButWireFyProvided_FiscalYearUsesWireFyNotEndYear()
    {
        // TryBuildParsedFact's three-step FiscalYear chain is
        // `resolved?.Year ?? value.Fy ?? value.End.Year`. The existing
        // `FiscalYearFallback` test pins the THIRD arm (both resolver and
        // value.Fy null → value.End.Year). The MIDDLE arm — resolver null
        // but value.Fy present and DIFFERENT from value.End.Year — is
        // unpinned. This case is structurally common: a fiscal Q1 ending
        // 2024-03-31 for a March-FYE filer like Apple is fiscal year 2024
        // per SEC's payload (Fy=2024), even though End.Year = 2024 by
        // coincidence — pick Fy=2023 with End=2024-03-31 so the two
        // diverge unambiguously. A refactor that "simplifies" the chain
        // to `resolved?.Year ?? value.End.Year` (dropping the wire Fy
        // because the fallback test still passes with End.Year on both
        // sides) would compile, pass FiscalYearFallback, and silently
        // misclassify every non-calendar-year fact whose stock lacks FYE
        // metadata — corrupting downstream year-keyed indices and the
        // unique (CommonStockId, FiscalYear, FiscalPeriod) tuple.
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
            End = new DateOnly(2024, 3, 31),
            Val = 100m,
            Accn = "0000320193-24-000123",
            Fy = 2023,
            Fp = "FY",
            Form = "10-K",
            Filed = new DateOnly(2024, 5, 1),
        };

        var method = serviceType.GetMethod(
            "TryBuildParsedFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var parsed = method!.Invoke(
            null,
            [FactTaxonomy.UsGaap, "Assets", "Assets", "USD", value, stock]
        );

        parsed.Should().NotBeNull();
        var fiscalYear = (int)parsedFactType!.GetProperty("FiscalYear")!.GetValue(parsed)!;
        fiscalYear.Should().Be(2023);
    }
}
