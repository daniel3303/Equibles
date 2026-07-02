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
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetMarketWide13FActivityNegativeMaxResultsTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetMarketWide13FActivityNegativeMaxResultsTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetMarketWide13FActivity_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var holder = new InstitutionalHolder { Cik = "1", Name = "Big Fund" };
        DbContext.AddRange(aapl, holder);
        DbContext.Add(MakeHolding(aapl, holder, prior, shares: 1_000, value: 1_000_000));
        DbContext.Add(MakeHolding(aapl, holder, current, shares: 1_500, value: 1_500_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        // Contract: maxResults is "Maximum number of stocks to return"; a negative value
        // is nonsensical input. It is forwarded unclamped into the EF Core
        // GetQuarterlyActivity(...).Take(maxResults) query, so a negative cap reaches
        // PostgreSQL as a negative LIMIT, which the engine rejects. The tool must degrade
        // gracefully rather than leak the executor's internal-error sentinel to the caller.
        var output = await sut.GetMarketWide13FActivity(bucket: "top-buys", maxResults: -1);

        output.Should().NotContain("An error occurred while executing");
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesFinancialDbContext ctx) =>
        new(
            new InstitutionalHoldingRepository(ctx),
            new InstitutionalHolderRepository(ctx),
            new CommonStockRepository(ctx),
            new StockSplitRepository(ctx),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(ctx),
                new StockSplitRepository(ctx)
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
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
