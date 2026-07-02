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

/// <summary>
/// Sibling to InstitutionalHoldingsToolsGetMarketWide13FActivityNegativeMaxResultsTests,
/// which pins the negative-maxResults guard for the top-buys / top-sells path inside
/// RenderMarketActivityMovers. The churn buckets (new-positions / sold-out-positions)
/// flow through RenderMarketActivityChurn — a structurally distinct method with its own
/// .Take(maxResults) sites. The contract is the same: maxResults is "Maximum number of
/// stocks to return", so a negative value must degrade gracefully rather than reach
/// PostgreSQL as a negative LIMIT and leak the executor's internal-error sentinel.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetMarketWide13FActivityChurnNegativeMaxResultsTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetMarketWide13FActivityChurnNegativeMaxResultsTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetMarketWide13FActivity_NewPositionsNegativeMaxResults_DoesNotSurfaceInternalError()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var msft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "C0",
        };
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var continuing = new InstitutionalHolder { Cik = "H0", Name = "Continuing Filer" };
        var newFiler = new InstitutionalHolder { Cik = "H1", Name = "New Filer" };
        DbContext.AddRange(msft, aapl, continuing, newFiler);
        // Continuing filer holds MSFT in both quarters → establishes a prior report date so
        // GetMarketWide13FActivity does not short-circuit on "no prior quarter".
        DbContext.Add(MakeHolding(msft, continuing, prior, shares: 500, value: 500_000));
        DbContext.Add(MakeHolding(msft, continuing, current, shares: 500, value: 500_000));
        // New filer holds AAPL only in the current quarter → a genuine new-position row, so the
        // churn query has rows to .Take() and a negative cap would actually reach the engine.
        DbContext.Add(MakeHolding(aapl, newFiler, current, shares: 100, value: 100_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(new InstitutionalHoldingRepository(verify)),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetMarketWide13FActivity(bucket: "new-positions", maxResults: -1);

        output.Should().NotContain("An error occurred while executing");
    }

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
