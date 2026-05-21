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
/// Pins the "No prior quarter to compare against" arm of
/// <c>ResolveMarketActivityDates</c> — the shared helper used by every
/// market-activity MCP tool (GetMarketWide13FActivity, GetMostHeldStocks).
/// When the database has exactly one report date, the helper must surface
/// a user-friendly error rather than silently picking the same date for
/// both `targetDate` and `previousDate` (which would dump a meaningless
/// zero-delta table). A regression that removed the
/// `targetIndex >= reportDates.Count - 1` guard would either index out of
/// range (current crash) or, worse, produce phantom zero-delta output.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsResolveMarketActivityDatesNoPriorQuarterTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsResolveMarketActivityDatesNoPriorQuarterTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetMostHeldStocks_OnlyOneReportDateExists_ReturnsNoPriorQuarterError()
    {
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var filer = new InstitutionalHolder { Cik = "H1", Name = "Sole Filer" };
        DbContext.AddRange(aapl, filer);
        DbContext.Add(MakeHolding(aapl, filer, current, shares: 100, value: 200_000));
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

        var output = await sut.GetMostHeldStocks();

        output.Should().Contain("No prior quarter to compare against");
        output.Should().Contain("2024-12-31");
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
