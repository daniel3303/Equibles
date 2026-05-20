using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins <c>GetTopBuyersSellers</c>. The tool ranks institutions by absolute Δ shares
/// versus the immediately prior 13F report date — buyers (Δ &gt; 0) descending, sellers
/// (Δ &lt; 0) ascending — and handles four boundary cases that the implementation has to
/// get right: a fresh new position (no prior row), a sold-out position (no current row),
/// an unchanged position (Δ = 0 → must be excluded from both lists), and the
/// no-prior-quarter case (every holder is a "new" buyer because the prior quarter is
/// empty).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetTopBuyersSellersTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetTopBuyersSellersTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetTopBuyersSellers_TickerWithQoQMovement_RanksBuyersDescAndSellersAsc()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var increaser = new InstitutionalHolder { Cik = "1", Name = "Increaser Inc." };
        var reducer = new InstitutionalHolder { Cik = "2", Name = "Reducer LLC" };
        var soldOut = new InstitutionalHolder { Cik = "3", Name = "Sold-Out Capital" };
        var newcomer = new InstitutionalHolder { Cik = "4", Name = "Newcomer Partners" };
        var steady = new InstitutionalHolder { Cik = "5", Name = "Steady Hands LP" };
        DbContext.Add(stock);
        DbContext.AddRange(increaser, reducer, soldOut, newcomer, steady);

        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);

        // Prior quarter: increaser/reducer/soldOut/steady each hold 1_000 shares.
        DbContext.Add(MakeHolding(stock, increaser, prior, shares: 1_000));
        DbContext.Add(MakeHolding(stock, reducer, prior, shares: 1_000));
        DbContext.Add(MakeHolding(stock, soldOut, prior, shares: 1_000));
        DbContext.Add(MakeHolding(stock, steady, prior, shares: 1_000));

        // Latest quarter: increaser → 1_500 (Δ +500), reducer → 800 (Δ -200),
        // soldOut → gone (Δ -1_000), newcomer → 2_000 (Δ +2_000), steady → 1_000 (Δ 0).
        DbContext.Add(MakeHolding(stock, increaser, latest, shares: 1_500));
        DbContext.Add(MakeHolding(stock, reducer, latest, shares: 800));
        DbContext.Add(MakeHolding(stock, newcomer, latest, shares: 2_000));
        DbContext.Add(MakeHolding(stock, steady, latest, shares: 1_000));
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

        var output = await sut.GetTopBuyersSellers("AAPL");

        // Headline + sections
        output.Should().Contain("Top buyers and sellers of Apple Inc. (AAPL) as of 2024-12-31");
        output.Should().Contain("vs prior quarter 2024-09-30");
        output.Should().Contain("## Top Buyers");
        output.Should().Contain("## Top Sellers");

        // Buyers: Newcomer first (Δ +2_000), then Increaser (Δ +500). Steady excluded (Δ 0).
        var buyersSection = output.Substring(
            output.IndexOf("## Top Buyers"),
            output.IndexOf("## Top Sellers") - output.IndexOf("## Top Buyers")
        );
        buyersSection
            .IndexOf("Newcomer Partners")
            .Should()
            .BeLessThan(buyersSection.IndexOf("Increaser Inc."));
        buyersSection.Should().Contain("+2,000");
        buyersSection.Should().Contain("+500");
        buyersSection.Should().NotContain("Steady Hands LP");

        // Sellers: Sold-Out first (Δ -1_000), then Reducer (Δ -200). Steady excluded.
        var sellersSection = output.Substring(output.IndexOf("## Top Sellers"));
        sellersSection
            .IndexOf("Sold-Out Capital")
            .Should()
            .BeLessThan(sellersSection.IndexOf("Reducer LLC"));
        sellersSection.Should().Contain("-1,000");
        sellersSection.Should().Contain("-200");
        sellersSection.Should().NotContain("Steady Hands LP");
    }

    [Fact]
    public async Task GetTopBuyersSellers_NoPriorQuarter_TreatsAllHoldersAsBuyers()
    {
        var stock = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        var a = new InstitutionalHolder { Cik = "10", Name = "Alpha Capital" };
        var b = new InstitutionalHolder { Cik = "11", Name = "Beta Capital" };
        DbContext.Add(stock);
        DbContext.AddRange(a, b);

        var only = new DateOnly(2024, 12, 31);
        DbContext.Add(MakeHolding(stock, a, only, shares: 5_000));
        DbContext.Add(MakeHolding(stock, b, only, shares: 3_000));
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

        var output = await sut.GetTopBuyersSellers("MSFT");

        // No prior quarter → both holders surface as buyers with their full positions.
        var buyersSection = output.Substring(
            output.IndexOf("## Top Buyers"),
            output.IndexOf("## Top Sellers") - output.IndexOf("## Top Buyers")
        );
        buyersSection
            .IndexOf("Alpha Capital")
            .Should()
            .BeLessThan(buyersSection.IndexOf("Beta Capital"));
        buyersSection.Should().Contain("+5,000");
        buyersSection.Should().Contain("+3,000");

        // No sellers when there is no prior quarter.
        output.Should().Contain("_No sellers this quarter._");
    }

    [Fact]
    public async Task GetTopBuyersSellers_UnknownTicker_ReportsStockNotFound()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopBuyersSellers("DOESNOTEXIST");

        output.Should().Contain("not found");
    }

    [Fact]
    public async Task GetTopBuyersSellers_StockExistsButHasNoHoldings_ReportsNoData()
    {
        var stock = new CommonStock
        {
            Ticker = "TSLA",
            Name = "Tesla Inc.",
            Cik = "0001318605",
        };
        DbContext.Add(stock);
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

        var output = await sut.GetTopBuyersSellers("TSLA");

        output.Should().Contain("No institutional holdings data");
    }

    [Fact]
    public async Task GetTopBuyersSellers_OnlyUnchangedHolders_ReportsNoMovement()
    {
        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corp.",
            Cik = "0001045810",
        };
        var holder = new InstitutionalHolder { Cik = "30", Name = "Steady State Capital" };
        DbContext.Add(stock);
        DbContext.Add(holder);

        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);
        // Same shares in both quarters → only the Unchanged bucket → no movers.
        DbContext.Add(MakeHolding(stock, holder, prior, shares: 1_000));
        DbContext.Add(MakeHolding(stock, holder, latest, shares: 1_000));
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

        var output = await sut.GetTopBuyersSellers("NVDA");

        // No buyers and no sellers — early-return message, not the per-section tables.
        output.Should().Contain("No quarter-over-quarter movement found");
        output.Should().NotContain("## Top Buyers");
        output.Should().NotContain("## Top Sellers");
    }

    [Fact]
    public async Task GetTopBuyersSellers_ExplicitReportDate_HonorsArgument()
    {
        var stock = new CommonStock
        {
            Ticker = "GOOG",
            Name = "Alphabet Inc.",
            Cik = "0001652044",
        };
        var holder = new InstitutionalHolder { Cik = "20", Name = "Targeted Capital" };
        DbContext.Add(stock);
        DbContext.Add(holder);

        var q3 = new DateOnly(2024, 9, 30);
        var q4 = new DateOnly(2024, 12, 31);
        DbContext.Add(MakeHolding(stock, holder, q3, shares: 100));
        DbContext.Add(MakeHolding(stock, holder, q4, shares: 500));
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

        // Explicit Q4 date — Δ is +400 vs the immediately prior Q3 row.
        var output = await sut.GetTopBuyersSellers("GOOG", reportDate: "2024-12-31");

        output.Should().Contain("as of 2024-12-31");
        output.Should().Contain("vs prior quarter 2024-09-30");
        output.Should().Contain("+400");
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
