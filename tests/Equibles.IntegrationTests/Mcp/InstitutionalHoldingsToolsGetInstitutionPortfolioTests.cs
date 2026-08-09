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

/// <summary>
/// Pins <c>GetInstitutionPortfolio</c> — the fourth and final InstitutionalHoldingsTools
/// tool. Unlike GetTopHolders which ranks by Shares, this method orders the
/// institution's portfolio rows by Value (market cap exposure, not raw share count).
/// A regression that copy-pasted the Shares-descending ordering from GetTopHolders
/// would silently re-rank an institution's portfolio against the wrong measure,
/// burying their biggest dollar positions behind low-priced penny stocks with
/// high share counts.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionPortfolioTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionPortfolioTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionPortfolio_BigValueLowSharesVsLowValueBigShares_RanksByValueDescending()
    {
        var holder = new InstitutionalHolder { Cik = "1", Name = "Berkshire Hathaway Inc." };
        var bigDollar = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var pennyStock = new CommonStock
        {
            Ticker = "PNY",
            Name = "Penny Stock Co.",
            Cik = "0001999999",
        };
        DbContext.Add(holder);
        DbContext.Add(bigDollar);
        DbContext.Add(pennyStock);

        var reportDate = new DateOnly(2024, 12, 31);
        // AAPL: fewer shares, but each share is worth more → larger Value.
        DbContext.Add(
            MakeHolding(holder, bigDollar, reportDate, shares: 1_000, value: 100_000_000)
        );
        // Penny: many more shares, but each is worth far less → smaller Value.
        DbContext.Add(
            MakeHolding(holder, pennyStock, reportDate, shares: 1_000_000, value: 1_000_000)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetInstitutionPortfolio("Berkshire");

        // AAPL is the high-Value, low-Shares row — it must rank first despite
        // having 1000× FEWER shares than the penny stock. A regression to
        // OrderByDescending(h => h.Shares) would flip these.
        var appleIdx = output.IndexOf("AAPL", StringComparison.Ordinal);
        var pennyIdx = output.IndexOf("PNY", StringComparison.Ordinal);
        appleIdx.Should().BeGreaterThan(0);
        pennyIdx.Should().BeGreaterThan(appleIdx, "rank must be by Value descending, not Shares");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_OffsetPaging_TiedValuesSplitCleanlyAcrossPages()
    {
        // Four rows tied on Value so only the stock-id/row-id tiebreaks order them —
        // exactly where a partial order would repeat or skip rows between offset pages.
        var holder = new InstitutionalHolder { Cik = "77", Name = "Paged Capital" };
        DbContext.Add(holder);
        var reportDate = new DateOnly(2024, 12, 31);
        var tickers = new[] { "PGA", "PGB", "PGC", "PGD" };
        foreach (var (ticker, index) in tickers.Select((t, i) => (t, i)))
        {
            var stock = new CommonStock
            {
                Ticker = ticker,
                Name = $"{ticker} Corp",
                Cik = $"000077000{index}",
            };
            DbContext.Add(stock);
            DbContext.Add(MakeHolding(holder, stock, reportDate, shares: 1_000, value: 5_000_000));
        }
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var page1 = await sut.GetInstitutionPortfolio("Paged Capital", maxResults: 2);
        var page2 = await sut.GetInstitutionPortfolio("Paged Capital", maxResults: 2, offset: 2);

        var onPage1 = tickers.Where(t => page1.Contains(t)).ToList();
        var onPage2 = tickers.Where(t => page2.Contains(t)).ToList();
        onPage1.Should().HaveCount(2);
        onPage2.Should().HaveCount(2);
        onPage1.Intersect(onPage2).Should().BeEmpty();
        onPage1.Concat(onPage2).Should().BeEquivalentTo(tickers);
        page2.Should().Contain("Showing holding rows 3-4 of 4");
        page2.Should().Contain("Showing results 3-4 of 4 (the last page).");
    }

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
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
            AccessionNumber = $"acc-{stock.Ticker}",
        };
}
