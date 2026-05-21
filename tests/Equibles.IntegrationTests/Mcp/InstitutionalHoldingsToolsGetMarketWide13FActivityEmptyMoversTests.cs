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
/// Pins <c>RenderMarketActivityMovers</c>'s empty-result fallback (line 617).
/// When the prior-quarter snapshot has zero shares changed in the requested
/// direction (top-buys / top-sells), the tool must surface an explicit
/// "no stocks moved" notice rather than emit a bare markdown table header
/// with no data rows. A regression that dropped the `rows.Count == 0` guard
/// would render `| # | Ticker | Company | Δ Shares | Δ Value ($M) |` with no
/// rows beneath it — a confusing dead table for the MCP consumer.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetMarketWide13FActivityEmptyMoversTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetMarketWide13FActivityEmptyMoversTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetMarketWide13FActivity_TopBuysWithNoSharesChanged_ReportsEmptyMessage()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var filer = new InstitutionalHolder { Cik = "H1", Name = "Sole Filer" };
        DbContext.AddRange(aapl, filer);
        // Identical holdings in both quarters — CurrentShares == PreviousShares, so the
        // `Where(a => a.CurrentShares != a.PreviousShares)` filter yields zero rows.
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

        var output = await sut.GetMarketWide13FActivity(bucket: "top-buys");

        output.Should().Contain("No stocks moved in this direction this quarter");
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
