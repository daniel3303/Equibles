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
/// Pins <c>InstitutionalHoldingsTools.GetMostHeldStocks</c> — the MCP companion
/// to the Holdings/MostHeld web page. The tool dispatches on the `sort`
/// argument over the same <c>GetMostHeld</c> + <c>GetUniqueFilerIds</c> queries;
/// each Fact exercises one branch (no data, unknown sort, default filers
/// ranking, filersDelta ranking, max cap).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetMostHeldStocksTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetMostHeldStocksTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetMostHeldStocks_NoData_ReportsNoHoldings()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMostHeldStocks();

        output.Should().Contain("No 13F holdings data");
    }

    [Fact]
    public async Task GetMostHeldStocks_UnknownSort_ReportsValidValues()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMostHeldStocks(sort: "garbage");

        output.Should().Contain("Unknown sort");
        output.Should().Contain("filers");
        output.Should().Contain("filersdelta");
        output.Should().Contain("value");
    }

    [Fact]
    public async Task GetMostHeldStocks_DefaultSort_RanksByFilerCountDescending()
    {
        await SeedThreeStockUniverse();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMostHeldStocks();

        output.Should().Contain("Most-held 13F stocks as of 2024-12-31");
        output.Should().Contain("Sorted by: filers");
        output.Should().Contain("5 filers in the 13F universe");
        output
            .IndexOf("AAPL", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("MSFT", StringComparison.Ordinal));
        output
            .IndexOf("MSFT", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("NVDA", StringComparison.Ordinal));
        // AAPL is held by 5 of 5 distinct filers → 100.0% of universe.
        output.Should().Contain("100.0%");
    }

    [Fact]
    public async Task GetMostHeldStocks_FilersDeltaSort_RanksByQoQFilerDeltaDescending()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var msft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "C2",
        };
        var holders = new InstitutionalHolder[6];
        for (var i = 0; i < holders.Length; i++)
            holders[i] = new InstitutionalHolder { Cik = $"H{i + 1}", Name = $"Filer {i + 1}" };
        DbContext.AddRange(aapl, msft);
        DbContext.AddRange(holders.Cast<object>().ToArray());

        // AAPL: prior 5 filers → current 6 filers (Δ +1, warming).
        for (var i = 0; i < 5; i++)
            DbContext.Add(MakeHolding(aapl, holders[i], prior, shares: 100, value: 100_000));
        for (var i = 0; i < 6; i++)
            DbContext.Add(MakeHolding(aapl, holders[i], current, shares: 100, value: 100_000));
        // MSFT: prior 1 filer → current 5 filers (Δ +4, hotter than AAPL).
        DbContext.Add(MakeHolding(msft, holders[0], prior, shares: 50, value: 50_000));
        for (var i = 0; i < 5; i++)
            DbContext.Add(MakeHolding(msft, holders[i], current, shares: 50, value: 50_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMostHeldStocks(sort: "filersDelta");

        output.Should().Contain("Sorted by: filersdelta");
        // MSFT (Δ +4) should appear before AAPL (Δ +1).
        output
            .IndexOf("MSFT", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("AAPL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetMostHeldStocks_MaxResults_TruncatesOutput()
    {
        await SeedThreeStockUniverse();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMostHeldStocks(maxResults: 2);

        // Only the top two (AAPL, MSFT) by filer count should appear.
        output.Should().Contain("AAPL");
        output.Should().Contain("MSFT");
        output.Should().NotContain("NVDA");
    }

    private async Task SeedThreeStockUniverse()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var msft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "C2",
        };
        var nvda = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corp.",
            Cik = "C3",
        };
        DbContext.AddRange(aapl, msft, nvda);

        var holders = new InstitutionalHolder[5];
        for (var i = 0; i < holders.Length; i++)
            holders[i] = new InstitutionalHolder { Cik = $"H{i + 1}", Name = $"Filer {i + 1}" };
        DbContext.AddRange(holders.Cast<object>().ToArray());

        // AAPL = 5 filers, MSFT = 3 filers, NVDA = 1 filer in the current quarter.
        for (var i = 0; i < 5; i++)
            DbContext.Add(MakeHolding(aapl, holders[i], current, shares: 100, value: 200_000));
        for (var i = 0; i < 3; i++)
            DbContext.Add(MakeHolding(msft, holders[i], current, shares: 50, value: 100_000));
        DbContext.Add(MakeHolding(nvda, holders[0], current, shares: 10, value: 20_000));
        // Anchor the prior quarter so ResolveMarketActivityDates finds a comparison.
        DbContext.Add(MakeHolding(aapl, holders[0], prior, shares: 100, value: 180_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesFinancialDbContext ctx) =>
        new(
            new InstitutionalHoldingRepository(ctx),
            new InstitutionalHolderRepository(ctx),
            new CommonStockRepository(ctx),
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
