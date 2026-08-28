using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
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
            new StockSplitRepository(DbContext),
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
        // Curated concepts the company never reported are omitted (dash-only
        // template rows are token noise), with one note flagging the omission.
        result.Should().NotContain("| Net Income |");
        result
            .Should()
            .Contain("_Line items the filer did not report for this period are omitted._");
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

        result.Should().Contain("has no income statement data for 2023 Q2");
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

    [Fact]
    public async Task GetFinancialStatement_PerShareLine_RestatesAcrossSplitWithoutChangingDollarLines()
    {
        var stock = Apple();
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenue",
        };
        var dilutedEps = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "EarningsPerShareDiluted",
            Label = "Diluted EPS",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().AddRange(revenue, dilutedEps);
        DbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    CommonStockId = stock.Id,
                    EffectiveDate = new DateOnly(2022, 6, 1),
                    Numerator = 4m,
                    Denominator = 1m,
                }
            );
        await SeedRevenue(
            stock,
            revenue,
            2021,
            SecFiscalPeriod.FullYear,
            100_000_000m,
            "USD",
            "revenue"
        );
        await SeedRevenue(
            stock,
            dilutedEps,
            2021,
            SecFiscalPeriod.FullYear,
            8m,
            "USD/shares",
            "eps"
        );

        var result = await Sut().GetFinancialStatement("AAPL", statement: "income", year: 2021);

        result.Should().Contain("| Revenue | $100,000,000 | USD |");
        result.Should().Contain("| EPS (Diluted) | $2.00 | USD/shares |");
        result.Should().NotContain("| EPS (Diluted) | $8.00 | USD/shares |");
        result.Should().Contain("Per-share values are split-adjusted");
    }

    [Fact]
    public async Task GetFinancialStatement_QuarterlyFlows_ShowExactSpansAndDropEarlierEndpoint()
    {
        var stock = Apple();
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenue",
        };
        var netIncome = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "NetIncomeLoss",
            Label = "Net income",
        };
        var grossProfit = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "GrossProfit",
            Label = "Gross profit",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().AddRange(revenue, netIncome, grossProfit);

        FinancialFact Fact(
            FinancialConcept concept,
            DateOnly start,
            DateOnly end,
            decimal value,
            string accession,
            SecFiscalPeriod fiscalPeriod = SecFiscalPeriod.Q2
        ) =>
            new()
            {
                CommonStockId = stock.Id,
                FinancialConceptId = concept.Id,
                Unit = "USD",
                PeriodType = FactPeriodType.Duration,
                PeriodStart = start,
                PeriodEnd = end,
                Value = value,
                FiscalYear = 2025,
                FiscalPeriod = fiscalPeriod,
                Form = DocumentType.TenQ,
                FiledDate = new DateOnly(2025, 8, 1),
                AccessionNumber = accession,
            };

        DbContext
            .Set<FinancialFact>()
            .AddRange(
                Fact(
                    revenue,
                    new DateOnly(2025, 4, 1),
                    new DateOnly(2025, 6, 30),
                    25_000_000m,
                    "revenue"
                ),
                Fact(
                    netIncome,
                    new DateOnly(2025, 1, 1),
                    new DateOnly(2025, 3, 31),
                    3_000_000m,
                    "net-income-q1",
                    SecFiscalPeriod.Q1
                ),
                Fact(
                    netIncome,
                    new DateOnly(2025, 1, 1),
                    new DateOnly(2025, 6, 30),
                    4_000_000m,
                    "net-income-ytd"
                ),
                Fact(
                    grossProfit,
                    new DateOnly(2025, 1, 1),
                    new DateOnly(2025, 3, 31),
                    99_000_000m,
                    "stale-end"
                )
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFinancialStatement("AAPL", statement: "income", year: 2025, period: "Q2");

        result
            .Should()
            .Contain("| Revenue | $25,000,000 | USD | Reported | 2025-04-01 | 2025-06-30 |");
        result
            .Should()
            .Contain(
                "| Net Income | $1,000,000 | USD | Derived quarter | 2025-04-01 | 2025-06-30 |"
            );
        result.Should().NotContain("$99,000,000");
        result.Should().Contain("All flow rows span one discrete quarter");
    }

    [Fact]
    public async Task GetFinancialStatement_LaterProxyDuplicate_DoesNotOutrankTenK()
    {
        var stock = Apple();
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenue",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().Add(revenue);
        await SeedRevenue(
            stock,
            revenue,
            2023,
            SecFiscalPeriod.FullYear,
            383_285_000_000m,
            "USD",
            "ten-k"
        );
        var proxy = await DbContext
            .Set<FinancialFact>()
            .SingleAsync(f => f.AccessionNumber == "ten-k");
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    CommonStockId = proxy.CommonStockId,
                    FinancialConceptId = proxy.FinancialConceptId,
                    Unit = proxy.Unit,
                    PeriodType = proxy.PeriodType,
                    PeriodStart = proxy.PeriodStart,
                    PeriodEnd = proxy.PeriodEnd,
                    Value = 383_000_000_000m,
                    FiscalYear = proxy.FiscalYear,
                    FiscalPeriod = proxy.FiscalPeriod,
                    Form = DocumentType.Def14A,
                    FiledDate = proxy.FiledDate.AddMonths(2),
                    AccessionNumber = "proxy",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFinancialStatement("AAPL", statement: "income", year: 2023, period: "FY");

        result.Should().Contain("$383,285,000,000");
        result.Should().Contain("| 10-K |");
        result.Should().NotContain("$383,000,000,000");
    }

    [Fact]
    public async Task GetFinancialStatement_OverlongLatestStampDoesNotBecomeTheDefaultPeriod()
    {
        var stock = Apple();
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenue",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().Add(revenue);
        await SeedRevenue(
            stock,
            revenue,
            2024,
            SecFiscalPeriod.FullYear,
            120m,
            "USD",
            "valid-fy24"
        );
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    CommonStockId = stock.Id,
                    FinancialConceptId = revenue.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2003, 5, 13),
                    PeriodEnd = new DateOnly(2025, 1, 24),
                    Value = 9_999m,
                    FiscalYear = 2025,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenQ,
                    FiledDate = new DateOnly(2025, 2, 1),
                    AccessionNumber = "overlong-fy25",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialStatement("AAPL", statement: "income");

        result.Should().Contain("FY2024 FY:");
        result.Should().Contain("| Revenue | $120 | USD |");
        result.Should().NotContain("$9,999");
    }

    [Fact]
    public async Task GetFinancialStatement_OverlongPreferredTagCannotHideAValidVariant()
    {
        var stock = Apple();
        var preferred = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "ResearchAndDevelopmentExpense",
        };
        var variant = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FinancialConcept>().AddRange(preferred, variant);

        FinancialFact Fact(
            FinancialConcept concept,
            DateOnly start,
            DateOnly end,
            decimal value,
            string accession
        ) =>
            new()
            {
                CommonStockId = stock.Id,
                FinancialConceptId = concept.Id,
                Unit = "USD",
                PeriodType = FactPeriodType.Duration,
                PeriodStart = start,
                PeriodEnd = end,
                Value = value,
                FiscalYear = 2025,
                FiscalPeriod = SecFiscalPeriod.FullYear,
                Form = DocumentType.TenK,
                FiledDate = new DateOnly(2026, 2, 1),
                AccessionNumber = accession,
            };

        DbContext
            .Set<FinancialFact>()
            .AddRange(
                Fact(
                    preferred,
                    new DateOnly(2003, 5, 13),
                    new DateOnly(2025, 12, 31),
                    9_999m,
                    "overlong-preferred"
                ),
                Fact(
                    variant,
                    new DateOnly(2025, 1, 1),
                    new DateOnly(2025, 12, 30),
                    200m,
                    "valid-variant"
                )
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFinancialStatement("AAPL", statement: "income", year: 2025, period: "FY");

        result.Should().Contain("$200");
        result.Should().NotContain("$9,999");
    }
}
