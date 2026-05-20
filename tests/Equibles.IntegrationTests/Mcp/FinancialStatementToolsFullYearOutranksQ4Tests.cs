using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling to <see cref="FinancialStatementToolsTests"/>. The private
/// <c>ChronologicalRank</c> helper documents the order
/// <c>Q1 &lt; Q2 &lt; Q3 &lt; Q4 &lt; FullYear</c>; combined with
/// <c>ResolveStatementPeriod</c>'s "first chronologically latest wins" rule,
/// a default call (no year, no period) against a stock that has BOTH Q4 and
/// FullYear data for the same fiscal year must pick FullYear. Existing
/// <c>MultipleYears_DefaultsToLatestAnnual</c> only exercises year ordering;
/// <c>RequestedPeriodMismatch</c> seeds only FullYear. A regression that
/// gave Q4 a higher rank than FullYear (or made them equal) would silently
/// substitute quarterly figures when annual is available.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialStatementToolsFullYearOutranksQ4Tests : ParadeDbMcpTestBase
{
    public FinancialStatementToolsFullYearOutranksQ4Tests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialStatementTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        );

    [Fact]
    public async Task GetFinancialStatement_BothQ4AndFullYearReported_DefaultsToFullYear()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().Add(revenue);
        DbContext
            .Set<FinancialFact>()
            .AddRange(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    FinancialConceptId = revenue.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2023, 10, 1),
                    PeriodEnd = new DateOnly(2023, 12, 31),
                    Value = 100_000_000_000m,
                    FiscalYear = 2023,
                    FiscalPeriod = SecFiscalPeriod.Q4,
                    Form = DocumentType.TenQ,
                    FiledDate = new DateOnly(2024, 1, 30),
                    AccessionNumber = "0000320193-24-000001",
                },
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    FinancialConceptId = revenue.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2023, 1, 1),
                    PeriodEnd = new DateOnly(2023, 12, 31),
                    Value = 400_000_000_000m,
                    FiscalYear = 2023,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = new DateOnly(2024, 2, 15),
                    AccessionNumber = "0000320193-24-000099",
                }
            );
        await DbContext.SaveChangesAsync();

        // No year, no period — must pick FullYear (highest ChronologicalRank).
        var result = await Sut().GetFinancialStatement("AAPL", statement: "income");

        result.Should().Contain("FY2023 FY:");
        result.Should().Contain("$400,000,000,000");
        result
            .Should()
            .NotContain(
                "$100,000,000,000",
                "Q4 must not outrank FullYear within the same fiscal year"
            );
    }
}
