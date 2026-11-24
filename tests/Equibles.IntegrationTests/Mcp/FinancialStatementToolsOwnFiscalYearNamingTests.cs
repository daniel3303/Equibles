using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// A January year-end retailer names the year ended 2026-02-01 its fiscal 2025, while the
/// period is stored under 2026, the calendar year it ends in. GetFinancialStatement prints the
/// company's own label, says why, and reads a year the caller types in that naming: asking for
/// 2025 returns the year that ended in February 2026, never the one before it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialStatementToolsOwnFiscalYearNamingTests : ParadeDbMcpTestBase
{
    public FinancialStatementToolsOwnFiscalYearNamingTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialStatementTools Sut() =>
        new(
            new StatementFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        );

    private async Task SeedHomeDepot(int? labelOffset)
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "HD",
            Name: "Home Depot, Inc.",
            Cik: "0000354950"
        );
        stock.FiscalYearLabelOffset = labelOffset;
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        DbContext.Set<EquityIssuer>().Add(stock);
        DbContext.Set<FinancialConcept>().Add(revenue);
        DbContext
            .Set<FinancialFact>()
            .AddRange(
                Year(
                    stock,
                    revenue,
                    2025,
                    new DateOnly(2024, 1, 29),
                    new DateOnly(2025, 2, 2),
                    159_514_000_000m
                ),
                Year(
                    stock,
                    revenue,
                    2026,
                    new DateOnly(2025, 2, 3),
                    new DateOnly(2026, 2, 1),
                    164_680_000_000m
                )
            );
        await DbContext.SaveChangesAsync();
    }

    private static FinancialFact Year(
        EquityIssuer stock,
        FinancialConcept concept,
        int storedYear,
        DateOnly start,
        DateOnly end,
        decimal value
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = stock.Id,
            FinancialConceptId = concept.Id,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = start,
            PeriodEnd = end,
            Value = value,
            FiscalYear = storedYear,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = end.AddDays(45),
            AccessionNumber = $"0000354950-{storedYear}-000001",
        };

    [Fact]
    public async Task GetFinancialStatement_Default_PrintsTheCompanysOwnYearAndSaysWhy()
    {
        await SeedHomeDepot(-1);

        var result = await Sut().GetFinancialStatement("HD", statement: "income");

        result.Should().Contain("— FY2025 FY:");
        result.Should().Contain("$164,680,000,000");
        result.Should().Contain("named for the calendar year it starts in");
        result.Should().NotContain("FY2026");
    }

    [Fact]
    public async Task GetFinancialStatement_TypedCompanyYear_ReadsThePeriodTheCompanyNamesSo()
    {
        await SeedHomeDepot(-1);

        var result = await Sut().GetFinancialStatement("HD", statement: "income", year: 2025);

        result.Should().Contain("— FY2025 FY:");
        result.Should().Contain("$164,680,000,000");
        result.Should().NotContain("$159,514,000,000");
    }

    [Fact]
    public async Task GetFinancialStatement_TypedYearPastTheLatest_NamesBothInTheCompanysNaming()
    {
        await SeedHomeDepot(-1);

        var result = await Sut().GetFinancialStatement("HD", statement: "income", year: 2026);

        result.Should().Contain("no income statement data for 2026");
        result.Should().Contain("Latest available: FY2025 FY");
    }

    [Fact]
    public async Task GetFinancialStatement_UnknownNaming_PrintsStoredYearsWithoutANote()
    {
        await SeedHomeDepot(null);

        var result = await Sut().GetFinancialStatement("HD", statement: "income", year: 2025);

        result.Should().Contain("— FY2025 FY:");
        result.Should().Contain("$159,514,000,000");
        result.Should().NotContain("named for the calendar year it starts in");
    }
}
