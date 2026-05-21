using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryBuildParsedFactWhitespaceAccnTests
{
    private static readonly MethodInfo TryBuildParsedFactMethod =
        typeof(FinancialFactsImportService).GetMethod(
            "TryBuildParsedFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // TryBuildParsedFact (extracted in #1495) has two documented reject paths:
    // unmappable Fp and missing Accn. The "Accn required" intent is clear from
    // the precedent set by GH-1350 (IsRecentFtdFile) and GH-1438 (ParseLine):
    // SEC accession numbers are stable identifiers in a known format; missing
    // ones are malformed input that should be skipped, not fabricated with a
    // garbage Accession field. The implementation uses `string.IsNullOrEmpty`,
    // which catches `null` and `""` but lets whitespace-only `"   "` through —
    // a regression mode where a SEC payload with a blanked accession produces
    // a ParsedFact whose Accession is whitespace, silently corrupting downstream
    // dedup-by-accession and the (AccessionNumber, ...) unique index lookups.
    [Fact]
    public void TryBuildParsedFact_WhitespaceOnlyAccn_ReturnsNull()
    {
        var value = new CompanyFactValue
        {
            Fp = "FY",
            Accn = "   ",
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

        var result = TryBuildParsedFactMethod.Invoke(
            null,
            [FactTaxonomy.UsGaap, "Revenues", "Revenues", "USD", value, stock]
        );

        result.Should().BeNull();
    }
}
