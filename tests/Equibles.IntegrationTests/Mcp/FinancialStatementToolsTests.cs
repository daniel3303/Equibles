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

[Collection(ParadeDbCollection.Name)]
public class FinancialStatementToolsTests : ParadeDbMcpTestBase
{
    public FinancialStatementToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialStatementTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        );

    private static CommonStock Apple() =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };

    [Fact]
    public async Task GetFinancialStatement_UnknownTicker_ReturnsNotFound()
    {
        var result = await Sut().GetFinancialStatement("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetFinancialStatement_UnknownStatement_ReturnsGuidance()
    {
        DbContext.Set<CommonStock>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialStatement("AAPL", statement: "wat");

        result.Should().Contain("Unknown statement 'wat'");
    }

    [Fact]
    public async Task GetFinancialStatement_NoFacts_ReturnsNotIngestedMessage()
    {
        DbContext.Set<CommonStock>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialStatement("AAPL", statement: "income");

        result.Should().Contain("No structured financial facts have been ingested for AAPL");
    }

    [Fact]
    public async Task GetFinancialStatement_SeededIncomeStatement_RendersLatestFiledTableAndDefaultsToLatest()
    {
        var stock = Apple();
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
                    PeriodStart = new DateOnly(2023, 1, 1),
                    PeriodEnd = new DateOnly(2023, 12, 31),
                    Value = 383_000_000_000m,
                    FiscalYear = 2023,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = new DateOnly(2024, 1, 15),
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
                    FiledDate = new DateOnly(2024, 6, 1),
                    AccessionNumber = "0000320193-24-000099",
                }
            );
        await DbContext.SaveChangesAsync();

        // No year/period given — must default to the latest reported period.
        var result = await Sut().GetFinancialStatement("AAPL", statement: "income");

        result.Should().Contain("Income Statement for AAPL (Apple Inc.) — FY2023 FY:");
        result.Should().Contain("| Revenue | $400,000,000,000 | USD |");
        result.Should().NotContain("$383,000,000,000", "the latest-filed restatement wins");
        // A curated concept the company never reported still renders as a
        // placeholder row so the statement keeps its shape.
        result.Should().Contain("| Net Income | — |");
    }

    private async Task SeedRevenue(
        CommonStock stock,
        FinancialConcept concept,
        int fiscalYear,
        SecFiscalPeriod period,
        decimal value,
        string unit,
        string accession
    )
    {
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    FinancialConceptId = concept.Id,
                    Unit = unit,
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(fiscalYear, 1, 1),
                    PeriodEnd = new DateOnly(fiscalYear, 12, 31),
                    Value = value,
                    FiscalYear = fiscalYear,
                    FiscalPeriod = period,
                    Form = DocumentType.TenK,
                    FiledDate = new DateOnly(fiscalYear + 1, 2, 1),
                    AccessionNumber = accession,
                }
            );
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFinancialStatement_ExplicitPeriodNeverReported_DoesNotSilentlyFallBack()
    {
        var stock = Apple();
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().Add(revenue);
        // Only an annual figure exists.
        await SeedRevenue(
            stock,
            revenue,
            2023,
            SecFiscalPeriod.FullYear,
            400_000_000_000m,
            "USD",
            "a-fy"
        );

        var result = await Sut()
            .GetFinancialStatement("AAPL", statement: "income", year: 2023, period: "Q2");

        result.Should().Contain("has no data for 2023 Q2");
        result.Should().Contain("Latest available: FY2023 FY");
        result
            .Should()
            .NotContain(
                "$400,000,000,000",
                "Q2 was requested but never reported — the annual figure must not be substituted"
            );
    }

    [Fact]
    public async Task GetFinancialStatement_MultipleYears_DefaultsToLatestAnnual()
    {
        var stock = Apple();
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().Add(revenue);
        await SeedRevenue(
            stock,
            revenue,
            2022,
            SecFiscalPeriod.FullYear,
            300_000_000_000m,
            "USD",
            "a-2022"
        );
        await SeedRevenue(
            stock,
            revenue,
            2023,
            SecFiscalPeriod.FullYear,
            400_000_000_000m,
            "USD",
            "a-2023"
        );

        var result = await Sut().GetFinancialStatement("AAPL", statement: "income");

        result.Should().Contain("FY2023 FY:");
        result.Should().Contain("$400,000,000,000");
        result.Should().NotContain("$300,000,000,000", "the latest year is the default");
    }

    [Fact]
    public async Task GetFinancialStatement_PerShareUnit_FormatsWithCents()
    {
        var stock = Apple();
        var eps = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "EarningsPerShareDiluted",
            Label = "EarningsPerShareDiluted",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().Add(eps);
        await SeedRevenue(stock, eps, 2023, SecFiscalPeriod.FullYear, 6.13m, "USD/shares", "a-eps");

        var result = await Sut().GetFinancialStatement("AAPL", statement: "income");

        result.Should().Contain("| EPS (Diluted) | $6.13 | USD/shares |");
    }
}
