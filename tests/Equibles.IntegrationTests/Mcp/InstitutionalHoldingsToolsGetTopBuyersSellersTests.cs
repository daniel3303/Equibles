using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Captures;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Mcp;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins <c>GetTopInstitutionalBuyersSellers</c>. The tool ranks institutions by absolute Δ shares
/// versus the immediately prior 13F report date — buyers (Δ &gt; 0) descending, sellers
/// (Δ &lt; 0) ascending — and handles four boundary cases that the implementation has to
/// get right: a fresh new position (no prior row), a sold-out position (no current row),
/// an unchanged position (Δ = 0 → must be excluded from both lists), and the
/// no-prior-quarter case (movement is unavailable without a comparison).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetTopInstitutionalBuyersSellersTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetTopInstitutionalBuyersSellersTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_TickerWithQoQMovement_RanksBuyersDescAndSellersAsc()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
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

        // Sold-Out Capital still FILED a 13F for the latest quarter (another position) — a
        // previous holder only counts as a seller when its current-quarter filing proves the
        // exit; a fund that just stopped filing (CIK migration/deregistration) is excluded.
        EquityIssuer otherStock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MSFT",
            Name: "Microsoft Corp.",
            Cik: "0000789019"
        );
        DbContext.Add(otherStock);
        DbContext.Add(MakeHolding(otherStock, soldOut, latest, shares: 10));
        // A prior 13F establishes that the newcomer's missing stock position was an absence.
        DbContext.Add(MakeHolding(otherStock, newcomer, prior, shares: 10));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        using var scope = McpResultCapture.Begin();
        var output = await sut.GetTopInstitutionalBuyersSellers("AAPL");

        // The counts cover every compared filer, and a card gets the rows as printed.
        output
            .Should()
            .Contain(
                "Among the filers compared, 2 added shares (1 new position) and 2 cut (1 sold out)."
            );
        var capture = scope.Get<InstitutionalActivityCapture>();
        (capture.ReportDate, capture.PreviousReportDate).Should().Be((latest, prior));
        capture.Counts.Should().Be(new InstitutionalActivityCounts(2, 1, 2, 1));
        capture
            .Buyers.Select(row =>
                (row.Name, row.Cik, row.PreviousShares, row.CurrentShares, row.DeltaShares)
            )
            .Should()
            .Equal(
                ("Newcomer Partners", "4", 0L, 2_000L, 2_000L),
                ("Increaser Inc.", "1", 1_000L, 1_500L, 500L)
            );
        capture
            .Sellers.Select(row => (row.Name, row.CurrentShares, row.DeltaShares))
            .Should()
            .Equal(("Sold-Out Capital", 0L, -1_000L), ("Reducer LLC", 800L, -200L));

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
        output.Should().Contain("Δ Position Value is the change in published position value");
        output.Should().Contain("may fall back to filer values");
        output.Should().NotContain("change in stored quarter-end position value");
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_NoPriorQuarter_ReportsUnavailableComparison()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MSFT",
            Name: "Microsoft Corp.",
            Cik: "0000789019"
        );
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
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopInstitutionalBuyersSellers("MSFT");

        output.Should().Contain("No prior quarter");
        output.Should().NotContain("## Top Buyers");
        output.Should().NotContain("+5,000");
        output.Should().NotContain("+3,000");
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_UnknownTicker_ReportsStockNotFound()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopInstitutionalBuyersSellers("DOESNOTEXIST");

        output.Should().Contain("not found");
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_StockExistsButHasNoHoldings_ReportsNoData()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TSLA",
            Name: "Tesla Inc.",
            Cik: "0001318605"
        );
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopInstitutionalBuyersSellers("TSLA");

        output.Should().Contain("No institutional holdings data");
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_OnlyUnchangedHolders_ReportsNoMovement()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NVDA",
            Name: "NVIDIA Corp.",
            Cik: "0001045810"
        );
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
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        using var scope = McpResultCapture.Begin();
        var output = await sut.GetTopInstitutionalBuyersSellers("NVDA");

        // No buyers and no sellers — early-return message, not the per-section tables, and no card.
        scope.Get<InstitutionalActivityCapture>().Should().BeNull();
        output.Should().Contain("No comparable quarter-over-quarter movement found");
        output.Should().NotContain("## Top Buyers");
        output.Should().NotContain("## Top Sellers");
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_CurrentZeroShareRowStillProvesAReportedExit()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "META",
            Name: "Meta Platforms Inc.",
            Cik: "0001326801"
        );
        var holder = new InstitutionalHolder { Cik = "31", Name = "Zero Row Capital" };
        DbContext.AddRange(stock, holder);

        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);
        DbContext.Add(MakeHolding(stock, holder, prior, shares: 1_000));
        DbContext.Add(MakeHolding(stock, holder, latest, shares: 0));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopInstitutionalBuyersSellers("META");

        output.Should().Contain("Zero Row Capital");
        output.Should().Contain("-1,000");
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_ExplicitReportDate_HonorsArgument()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GOOG",
            Name: "Alphabet Inc.",
            Cik: "0001652044"
        );
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
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        // Explicit Q4 date — Δ is +400 vs the immediately prior Q3 row.
        var output = await sut.GetTopInstitutionalBuyersSellers("GOOG", reportDate: "2024-12-31");

        output.Should().Contain("as of 2024-12-31");
        output.Should().Contain("vs prior quarter 2024-09-30");
        output.Should().Contain("+400");
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_ReverseSplitBetweenQuarters_LeavesOutTheCounts()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "RVSP",
            Name: "Reverse Split Corp.",
            Cik: "0001999001"
        );
        var buyer = new InstitutionalHolder { Cik = "61", Name = "Real Buyer LP" };
        var passive = new InstitutionalHolder { Cik = "62", Name = "Passive Index Trust" };
        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);
        DbContext.AddRange(
            stock,
            buyer,
            passive,
            MakeHolding(stock, buyer, prior, shares: 10_000),
            MakeHolding(stock, buyer, latest, shares: 5_000),
            // 1,005 shares become 100.5 in a 1-for-10 reverse split: the filer reports 100 after
            // cash in lieu, while the restated prior count rounds to 101.
            MakeHolding(stock, passive, prior, shares: 1_005),
            MakeHolding(stock, passive, latest, shares: 100),
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EffectiveDate = new DateOnly(2024, 11, 1),
                Numerator = 1,
                Denominator = 10,
                PriceSeriesTicker = "RVSP",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        using var scope = McpResultCapture.Begin();
        var output = await sut.GetTopInstitutionalBuyersSellers("RVSP");

        // The ranking still lists the real buyer first; a count of "cutting" holders would
        // report the passive holder's rounded share as a sale.
        output.Should().Contain("Real Buyer LP");
        output.Should().NotContain("Among the filers compared");
        var capture = scope.Get<InstitutionalActivityCapture>();
        capture.Counts.Should().BeNull();
        capture.Buyers.Should().ContainSingle().Which.DeltaShares.Should().Be(4_000);
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_FractionalSplitThenALaterWholeOne_StillLeavesOutTheCounts()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "FRSP",
            Name: "Fractional Split Corp.",
            Cik: "0001999002"
        );
        var buyer = new InstitutionalHolder { Cik = "71", Name = "Real Buyer LP" };
        var passive = new InstitutionalHolder { Cik = "72", Name = "Passive Index Trust" };
        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);
        DbContext.AddRange(
            stock,
            buyer,
            passive,
            MakeHolding(stock, buyer, prior, shares: 1_000),
            MakeHolding(stock, buyer, latest, shares: 3_000),
            // 1,001 shares become 1,501.5 in the 3-for-2 split and the filer reports 1,501. The
            // later 2-for-1 makes the earlier quarter's factor a whole 3, yet 3,003 against 3,002
            // still reads as a one-share sale.
            MakeHolding(stock, passive, prior, shares: 1_001),
            MakeHolding(stock, passive, latest, shares: 1_501),
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EffectiveDate = new DateOnly(2024, 11, 1),
                Numerator = 3,
                Denominator = 2,
                PriceSeriesTicker = "FRSP",
            },
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EffectiveDate = new DateOnly(2025, 6, 2),
                Numerator = 2,
                Denominator = 1,
                PriceSeriesTicker = "FRSP",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        using var scope = McpResultCapture.Begin();
        var output = await sut.GetTopInstitutionalBuyersSellers("FRSP");

        output.Should().NotContain("Among the filers compared");
        var capture = scope.Get<InstitutionalActivityCapture>();
        capture.Counts.Should().BeNull();
        capture.Buyers.Should().ContainSingle().Which.DeltaShares.Should().Be(3_000);
    }

    [Fact]
    public async Task GetTopInstitutionalBuyersSellers_PrimaryRowsIgnoreSiblingListingSplits()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GOOGL",
            Name: "Alphabet Inc.",
            Cik: "0001652044"
        );
        var holder = new InstitutionalHolder { Cik = "21", Name = "Class A Capital" };
        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);
        DbContext.AddRange(
            stock,
            holder,
            MakeHolding(stock, holder, prior, shares: 1_000),
            MakeHolding(stock, holder, latest, shares: 1_000),
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EffectiveDate = new DateOnly(2024, 10, 1),
                Numerator = 20,
                Denominator = 1,
                PriceSeriesTicker = "GOOG",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopInstitutionalBuyersSellers("GOOGL");

        output.Should().Contain("No comparable quarter-over-quarter movement found");
    }

    private static InstitutionalHolding MakeHolding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
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
