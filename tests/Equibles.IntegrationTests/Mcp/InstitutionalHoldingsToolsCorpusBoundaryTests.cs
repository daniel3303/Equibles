using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsCorpusBoundaryTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsCorpusBoundaryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetMostHeldStocks_FirstCoveredQuarter_HidesFabricatedDeltas()
    {
        var prior = new DateOnly(2019, 12, 31);
        var current = new DateOnly(2020, 3, 31);
        var stock = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft",
            Cik = "C1",
        };
        var sparse = new InstitutionalHolder { Cik = "H1", Name = "Sparse filer" };
        var currentOnly = new InstitutionalHolder { Cik = "H2", Name = "Current filer" };
        DbContext.AddRange(stock, sparse, currentOnly);
        DbContext.Add(MakeHolding(stock, sparse, prior, 1, 1));
        DbContext.Add(MakeHolding(stock, sparse, current, 100, 100_000));
        DbContext.Add(MakeHolding(stock, currentOnly, current, 200, 200_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var read = Fixture.CreateDbContext();
        var output = await NewSut(read).GetMostHeldStocks("2020-03-31");

        output.Should().Contain("MSFT");
        output.Should().Contain("No prior report quarter is available within complete coverage");
        output.Should().NotContain("Prior quarter: 2019-12-31");
        output.Should().Contain("Delta columns are shown as —");
        output.Should().NotContain("| +1 | 0.3 | +0.3 |");
    }

    [Fact]
    public async Task GetMarketWide13FActivity_FirstCoveredQuarter_ReturnsNoComparisonRanking()
    {
        var prior = new DateOnly(2019, 12, 31);
        var current = new DateOnly(2020, 3, 31);
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cik = "C1",
        };
        var filer = new InstitutionalHolder { Cik = "H1", Name = "Filer" };
        DbContext.AddRange(stock, filer);
        DbContext.Add(MakeHolding(stock, filer, prior, 1, 1));
        DbContext.Add(MakeHolding(stock, filer, current, 10_000, 10_000_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var read = Fixture.CreateDbContext();
        var output = await NewSut(read).GetMarketWide13FActivity("top-buys", "2020-03-31");

        output.Should().Contain("No ranking is available");
        output.Should().Contain("complete coverage, which begins 2020-03-31");
        output.Should().NotContain("AAPL");
    }

    [Fact]
    public async Task GetMostHeldStocks_TargetBeforeCoverage_RejectsSparseRanking()
    {
        var prior = new DateOnly(2019, 9, 30);
        var current = new DateOnly(2019, 12, 31);
        var stock = new CommonStock
        {
            Ticker = "ARE",
            Name = "Alexandria",
            Cik = "C1",
        };
        var filer = new InstitutionalHolder { Cik = "H1", Name = "Filer" };
        DbContext.AddRange(stock, filer);
        DbContext.Add(MakeHolding(stock, filer, prior, 1, 0));
        DbContext.Add(MakeHolding(stock, filer, current, 1, 0));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var read = Fixture.CreateDbContext();
        var output = await NewSut(read).GetMostHeldStocks("2019-12-31");

        output.Should().Contain("ranking for 2019-12-31 is unavailable");
        output.Should().Contain("Complete 13F coverage begins with 2020-03-31");
        output.Should().NotContain("ARE |");
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesFinancialDbContext dbContext) =>
        new(
            new InstitutionalHoldingRepository(dbContext),
            new InstitutionalHolderRepository(dbContext),
            new CommonStockRepository(dbContext),
            new StockSplitRepository(dbContext),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(dbContext),
                new StockSplitRepository(dbContext)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{stock.Ticker}-{reportDate:yyyyMMdd}",
        };
}
