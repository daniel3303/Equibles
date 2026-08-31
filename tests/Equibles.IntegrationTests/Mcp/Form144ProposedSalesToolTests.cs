using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.Mcp;

public class Form144ProposedSalesToolTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InsiderTradingTools _tools;

    public Form144ProposedSalesToolTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            // GetForm144ProposedSales restates the percent numerator onto today's split
            // basis, so the tool now reads StockSplit rows.
            new CorporateActionsModuleConfiguration(),
            new InsiderTradingModuleConfiguration()
        );
        _tools = new InsiderTradingTools(
            new InsiderTransactionRepository(_dbContext),
            new InsiderOwnerRepository(_dbContext),
            new Form144FilingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            errorManager: null,
            NullLogger<InsiderTradingTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private CommonStock SeedStock(
        string ticker = "AAPL",
        string cik = "0000320193",
        long sharesOutstanding = 0
    )
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = "Apple Inc.",
            Cik = cik,
            SharesOutStanding = sharesOutstanding,
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.SaveChanges();
        return stock;
    }

    [Fact]
    public async Task GetForm144ProposedSales_StockNotFound_ReturnsNotFoundMessage()
    {
        var result = await _tools.GetForm144ProposedSales("ZZZZ");

        result.Should().Contain("ZZZZ");
    }

    [Fact]
    public async Task GetForm144ProposedSales_NoFilings_ReturnsEmptyMessage()
    {
        SeedStock();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain("No Form 144 proposed sales found for AAPL.");
    }

    [Fact]
    public async Task GetForm144ProposedSales_WithFilings_RendersTableNewestFirst()
    {
        var stock = SeedStock();
        _dbContext
            .Set<Form144Filing>()
            .Add(MakeFiling(stock.Id, "older", new DateOnly(2026, 1, 5), "ALICE", 1000));
        _dbContext
            .Set<Form144Filing>()
            .Add(
                MakeFiling(stock.Id, "newer", new DateOnly(2026, 5, 27), "LEVINSON ARTHUR D", 50000)
            );
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain("Apple Inc.");
        result.Should().Contain("LEVINSON ARTHUR D");
        result.Should().Contain("50,000"); // invariant-culture grouping
        result.Should().Contain("Director");
        // Newest filing renders before the older one.
        result
            .IndexOf("LEVINSON ARTHUR D", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("ALICE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetForm144ProposedSales_TiedDates_UseAccessionBeforeTheLimit()
    {
        var stock = SeedStock();
        var day = new DateOnly(2026, 5, 27);
        _dbContext
            .Set<Form144Filing>()
            .AddRange(
                MakeFiling(stock.Id, "0004", day, "DELTA SELLER", 400),
                MakeFiling(stock.Id, "0003", day, "CHARLIE SELLER", 300),
                MakeFiling(stock.Id, "0002", day, "BRAVO SELLER", 200),
                MakeFiling(stock.Id, "0001", day, "ALPHA SELLER", 100)
            );
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL", maxResults: 2);

        result.Should().Contain("ALPHA SELLER");
        result.Should().Contain("BRAVO SELLER");
        result.Should().NotContain("CHARLIE SELLER");
        result.Should().NotContain("DELTA SELLER");
    }

    [Fact]
    public async Task GetForm144ProposedSales_RespectsMaxResults()
    {
        var stock = SeedStock();
        for (var i = 0; i < 5; i++)
        {
            _dbContext
                .Set<Form144Filing>()
                .Add(
                    MakeFiling(
                        stock.Id,
                        $"acc-{i}",
                        new DateOnly(2026, 1, 1).AddDays(i),
                        "SELLER",
                        100
                    )
                );
        }
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL", maxResults: 2);

        result.Should().Contain("Showing notices 1-2 of 5, newest first");
        result.Should().Contain("Showing results 1-2 of 5");
    }

    [Fact]
    public async Task GetForm144ProposedSales_AllNoticesShown_OmitsTruncationNote()
    {
        var stock = SeedStock();
        _dbContext
            .Set<Form144Filing>()
            .Add(MakeFiling(stock.Id, "acc", new DateOnly(2026, 1, 5), "ALICE", 1000));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain("Showing notices 1-1 of 1, newest first");
        result.Should().NotContain("raise maxResults");
    }

    [Fact]
    public async Task GetForm144ProposedSales_DateRange_FiltersByFilingDate()
    {
        var stock = SeedStock();
        _dbContext
            .Set<Form144Filing>()
            .Add(MakeFiling(stock.Id, "early", new DateOnly(2026, 1, 10), "EARLY SELLER", 1000));
        _dbContext
            .Set<Form144Filing>()
            .Add(MakeFiling(stock.Id, "late", new DateOnly(2026, 6, 10), "LATE SELLER", 2000));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales(
            "AAPL",
            fromDate: "2026-03-01",
            toDate: "2026-12-31"
        );

        result.Should().Contain("LATE SELLER");
        result.Should().NotContain("EARLY SELLER");
        result.Should().Contain("Showing notices 1-1 of 1, newest first");
    }

    [Fact]
    public async Task GetForm144ProposedSales_DateRangeWithoutMatches_NamesTheAppliedRange()
    {
        var stock = SeedStock();
        _dbContext
            .Set<Form144Filing>()
            .Add(MakeFiling(stock.Id, "old", new DateOnly(2026, 1, 10), "EARLY SELLER", 1000));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales(
            "AAPL",
            fromDate: " 2026-03-01 ",
            toDate: ""
        );

        result.Should().Contain("match the requested filing-date range");
        result.Should().Contain("fromDate=2026-03-01, toDate=unbounded");
        result.Should().NotBe("No Form 144 proposed sales found for AAPL.");
    }

    [Fact]
    public async Task GetForm144ProposedSales_OffsetPagesNewestFirst_AndRejectsPastEnd()
    {
        var stock = SeedStock();
        _dbContext
            .Set<Form144Filing>()
            .AddRange(
                MakeFiling(stock.Id, "old", new DateOnly(2026, 1, 1), "OLD SELLER", 100),
                MakeFiling(stock.Id, "middle", new DateOnly(2026, 2, 1), "MIDDLE SELLER", 100),
                MakeFiling(stock.Id, "new", new DateOnly(2026, 3, 1), "NEW SELLER", 100)
            );
        await _dbContext.SaveChangesAsync();

        var page = await _tools.GetForm144ProposedSales("AAPL", maxResults: 1, offset: 1);
        var pastEnd = await _tools.GetForm144ProposedSales("AAPL", offset: 3);

        page.Should()
            .Contain("MIDDLE SELLER")
            .And.NotContain("NEW SELLER")
            .And.NotContain("OLD SELLER");
        page.Should().Contain("Showing results 2-2 of 3");
        pastEnd.Should().Contain("No results at offset 3 - only 3 Form 144 notices match");
    }

    [Fact]
    public async Task GetForm144ProposedSales_MalformedDate_ReturnsAcceptedFormatError()
    {
        SeedStock();

        var result = await _tools.GetForm144ProposedSales("AAPL", toDate: "June 2026");

        result.Should().Contain("Unknown toDate 'June 2026'");
        result.Should().Contain("yyyy-MM-dd");
    }

    [Fact]
    public async Task GetForm144ProposedSales_InvertedDateRange_ReturnsExplicitError()
    {
        SeedStock();

        var result = await _tools.GetForm144ProposedSales(
            "AAPL",
            fromDate: "2026-12-31",
            toDate: "2026-01-01"
        );

        result.Should().Be("fromDate must be on or before toDate.");
    }

    [Fact]
    public async Task GetForm144ProposedSales_RendersPercentOfIssuerSharesOutstanding()
    {
        var stock = SeedStock(sharesOutstanding: 2_000_000_000);
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2026, 1, 5), "ALICE", 1_000_000);
        // Filer typed their own sale count into noOfUnitsOutstanding (the MSFT
        // "100% of outstanding" bug, #7164 EquiblesCommercial) — the notice's field
        // must NOT be the denominator.
        filing.SharesOutstanding = 1_000_000;
        _dbContext.Set<Form144Filing>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        // 1,000,000 / 2,000,000,000 (the issuer record's share count) = 0.05%.
        result.Should().Contain("% Outstanding");
        result.Should().Contain("| 0.05% |");
        result.Should().NotContain("| 100% |");
    }

    [Fact]
    public async Task GetForm144ProposedSales_NoIssuerShareCount_RendersDash()
    {
        // Issuer record carries no share count — serve "-" rather than trusting the
        // notice's self-reported field.
        var stock = SeedStock(sharesOutstanding: 0);
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2026, 1, 5), "ALICE", 1000);
        filing.SharesOutstanding = 2_000_000_000;
        _dbContext.Set<Form144Filing>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain("| - |");
    }

    [Fact]
    public async Task GetForm144ProposedSales_NoticeBeforeSplit_PercentUsesTheSplitAdjustedShareCount()
    {
        // The filed share count sits on the pre-split basis while the issuer record's
        // count is current — restate the numerator so the ratio compares like with like
        // (10,000 pre-split shares = 100,000 post-split ÷ 2,000,000,000 = 0.005%).
        var stock = SeedStock(sharesOutstanding: 2_000_000_000);
        _dbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    CommonStockId = stock.Id,
                    EffectiveDate = new DateOnly(2026, 3, 1),
                    Numerator = 10,
                    Denominator = 1,
                    Source = StockSplitSource.Yahoo,
                }
            );
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2026, 1, 5), "ALICE", 10_000);
        _dbContext.Set<Form144Filing>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        // The Shares column stays as filed; only the percent numerator is restated.
        result.Should().Contain("| 10,000 |");
        result.Should().Contain("| 0.005% |");
    }

    [Fact]
    public async Task GetForm144ProposedSales_RemarksPreserveCompleteUnicodeText()
    {
        var stock = SeedStock();
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2026, 1, 5), "ALICE", 1000);
        filing.Remarks = new string('a', 89) + "😀 trailing text";
        _dbContext.Set<Form144Filing>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain(new string('a', 89) + "😀 trailing text");
    }

    [Fact]
    public async Task GetForm144ProposedSales_RemarksPreserveCompleteEscapedPipeText()
    {
        var stock = SeedStock();
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2026, 1, 5), "ALICE", 1000);
        filing.Remarks = new string('a', 89) + "|trailing text";
        _dbContext.Set<Form144Filing>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain(new string('a', 89) + "\\|trailing text");
    }

    [Fact]
    public async Task GetForm144ProposedSales_RemarksPreserveLateRule10b5OneDisclosure()
    {
        var stock = SeedStock();
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2026, 1, 5), "ALICE", 1000);
        filing.Remarks =
            new string('a', 120) + " Sale will be made pursuant to a Rule 10b5-1 plan.";
        _dbContext.Set<Form144Filing>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain("Sale will be made pursuant to a Rule 10b5-1 plan.");
    }

    [Fact]
    public async Task GetForm144ProposedSales_RemarksBackslashBeforePipe_KeepsPipeInsideCell()
    {
        var stock = SeedStock();
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2026, 1, 5), "ALICE", 1000);
        filing.Remarks = "Sale under plan A\\|renewed";
        _dbContext.Set<Form144Filing>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain("Sale under plan A\\\\\\|renewed");
    }

    [Fact]
    public async Task GetForm144ProposedSales_AllFiledTextCellsStayInsideTheirColumns()
    {
        var stock = SeedStock();
        var filing = MakeFiling(
            stock.Id,
            "acc",
            new DateOnly(2026, 1, 5),
            "SELLER\\|NAME\nSECOND",
            1000
        );
        filing.RelationshipToIssuer = "Officer\\|Director\nAffiliate";
        filing.BrokerName = "BROKER\\|DESK\nLLC";
        _dbContext.Set<Form144Filing>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetForm144ProposedSales("AAPL");

        result.Should().Contain("SELLER\\\\\\|NAME SECOND");
        result.Should().Contain("Officer\\\\\\|Director Affiliate");
        result.Should().Contain("BROKER\\\\\\|DESK LLC");
    }

    private static Form144Filing MakeFiling(
        Guid stockId,
        string accession,
        DateOnly filingDate,
        string seller,
        long shares
    )
    {
        return new Form144Filing
        {
            CommonStockId = stockId,
            AccessionNumber = accession,
            FilingDate = filingDate,
            SellerName = seller,
            RelationshipToIssuer = "Director",
            SecurityClassTitle = "Common",
            BrokerName = "Charles Schwab & Co., Inc.",
            SharesToBeSold = shares,
            AggregateMarketValue = shares * 300m,
            SharesOutstanding = 14687356000,
            ApproxSaleDate = filingDate,
            SecuritiesExchangeName = "NASDAQ",
        };
    }
}
