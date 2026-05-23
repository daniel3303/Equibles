using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Financials tab (#946). An existing ticker must resolve via
/// <c>LoadStock(ticker.ToUpper())</c>, render the shared "Show" view with
/// <c>ActiveTab == "financials"</c>, and stash a populated
/// <see cref="FinancialsTabViewModel"/> whose rows follow the curated
/// <see cref="Equibles.Sec.FinancialFacts.Data.Statements.FinancialStatementConcepts"/>
/// order. Two filings of the same concept are seeded to assert the latest-filed
/// (restated) value is the one surfaced — the contract the tab shares with the
/// MCP tool (#875).
/// </summary>
public class StocksControllerFinancialsTabTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;

    public StocksControllerFinancialsTabTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new FinancialFactsModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new FinraModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new YahooModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Financials_ExistingTickerWithFacts_ReturnsShowViewWithLatestFiledIncomeStatement()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var revenueConcept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<FinancialConcept>().Add(revenueConcept);
        // Same concept/period reported twice; the restatement (later FiledDate)
        // carries 400 and must win over the original 383.
        _dbContext
            .Set<FinancialFact>()
            .AddRange(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    FinancialConceptId = revenueConcept.Id,
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
                    FinancialConceptId = revenueConcept.Id,
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
        await _dbContext.SaveChangesAsync();

        var stockTabService = new StockTabService(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext)
        );
        var controller = new StocksController(
            new CommonStockRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new InstitutionalHoldingRepository(_dbContext),
            new DocumentRepository(_dbContext),
            stockTabService,
            Substitute.For<ILogger<StocksController>>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        // Lowercase ticker exercises LoadStock's ToUpper() normalisation.
        var result = await controller.Financials(
            "aapl",
            FinancialStatementType.IncomeStatement,
            year: null,
            period: null
        );

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Show");
        view.Model.Should()
            .BeOfType<StockDetailViewModel>()
            .Which.ActiveTab.Should()
            .Be("financials");

        var tab = controller
            .ViewData["TabViewModel"]
            .Should()
            .BeOfType<FinancialsTabViewModel>()
            .Subject;
        tab.HasData.Should().BeTrue();
        tab.SelectedYear.Should().Be(2023);
        tab.SelectedPeriod.Should().Be(SecFiscalPeriod.FullYear);
        tab.AvailablePeriods.Should().ContainSingle();

        var revenueLine = tab.Lines.Should().Contain(l => l.Label == "Revenue").Subject;
        revenueLine.HasValue.Should().BeTrue();
        revenueLine.Value.Should().Be(400_000_000_000m, "the latest-filed restatement wins");
        revenueLine.Unit.Should().Be("USD");

        // A curated concept the company never reported still renders, valueless,
        // so the statement keeps its shape.
        tab.Lines.Should().Contain(l => l.Label == "Net Income" && !l.HasValue);
    }

    [Fact]
    public async Task Financials_MultiplePeriods_OrdersChronologicallyAndDefaultsToLatestAnnual()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var concept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<FinancialConcept>().Add(concept);

        FinancialFact Fact(int fy, SecFiscalPeriod fp, string accn) =>
            new()
            {
                Id = Guid.NewGuid(),
                CommonStockId = stock.Id,
                FinancialConceptId = concept.Id,
                Unit = "USD",
                PeriodType = FactPeriodType.Duration,
                PeriodStart = new DateOnly(fy, 1, 1),
                PeriodEnd = new DateOnly(fy, 12, 31),
                Value = 1m,
                FiscalYear = fy,
                FiscalPeriod = fp,
                Form = DocumentType.TenK,
                FiledDate = new DateOnly(fy + 1, 2, 1),
                AccessionNumber = accn,
            };

        // Seed deliberately out of order, mixing an annual and a quarter in the
        // same year, to exercise the chronological-rank ordering (enum ordinal
        // would float FY to the wrong end).
        _dbContext
            .Set<FinancialFact>()
            .AddRange(
                Fact(2022, SecFiscalPeriod.FullYear, "a-2022-fy"),
                Fact(2023, SecFiscalPeriod.Q1, "a-2023-q1"),
                Fact(2023, SecFiscalPeriod.FullYear, "a-2023-fy")
            );
        await _dbContext.SaveChangesAsync();

        var stockTabService = new StockTabService(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext)
        );

        var tab = await stockTabService.LoadFinancialsTab(
            stock,
            FinancialStatementType.IncomeStatement,
            year: null,
            period: null
        );

        tab.AvailablePeriods.Select(p => p.Token)
            .Should()
            .ContainInOrder("2023-FullYear", "2023-Q1", "2022-FullYear");
        // Default selection is the first option — the latest year's annual.
        tab.SelectedYear.Should().Be(2023);
        tab.SelectedPeriod.Should().Be(SecFiscalPeriod.FullYear);
    }
}
