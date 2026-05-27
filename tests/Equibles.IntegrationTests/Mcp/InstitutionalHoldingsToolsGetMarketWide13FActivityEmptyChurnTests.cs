using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling to InstitutionalHoldingsToolsGetMarketWide13FActivityEmptyMoversTests
/// (which pins the top-buys / top-sells empty-rows fallback inside
/// RenderMarketActivityMovers). The churn buckets (new-positions /
/// sold-out-positions) flow through RenderMarketActivityChurn — a structurally
/// distinct method with its own `rows.Count == 0` guard and its own empty-state
/// message ("No stocks in this bucket this quarter"). A refactor that dropped
/// the guard would emit a bare markdown table header for new-positions when no
/// filer initiated a position, confusing the MCP consumer with a dead table.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetMarketWide13FActivityEmptyChurnTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetMarketWide13FActivityEmptyChurnTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetMarketWide13FActivity_NewPositionsWithNoInitiatedFilers_ReportsEmptyMessage()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var filer = new InstitutionalHolder { Cik = "H1", Name = "Continuing Filer" };
        DbContext.AddRange(aapl, filer);
        // Same filer holds the same stock in both quarters → no new-positions, no sold-outs.
        DbContext.Add(MakeHolding(aapl, filer, prior, shares: 100, value: 100_000));
        DbContext.Add(MakeHolding(aapl, filer, current, shares: 100, value: 100_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetMarketWide13FActivity(bucket: "new-positions");

        output.Should().Contain("No stocks in this bucket this quarter");
        output.Should().NotContain("| # | Ticker |");
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
