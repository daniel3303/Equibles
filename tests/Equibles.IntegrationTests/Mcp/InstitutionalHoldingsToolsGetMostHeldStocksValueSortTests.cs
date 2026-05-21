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
/// Sibling pin to the existing DefaultSort and FilersDeltaSort tests. The
/// "value" arm of <c>GetMostHeldStocks</c> is the third entry in
/// <c>ValidMostHeldSorts</c> and is wired through its own switch case
/// (OrderByDescending CurrentValue, then CurrentFilerCount). A regression
/// that dropped the case would fall through to the default `filers` arm,
/// silently reordering the output behind a still-correct "Sorted by:
/// value" header. Setup distinguishes the two arms by inverting the
/// ranking: AAPL has the highest dollar value but the lowest filer count;
/// MSFT has the highest filer count but a lower dollar value.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetMostHeldStocksValueSortTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetMostHeldStocksValueSortTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetMostHeldStocks_ValueSort_RanksByCurrentValueDescending()
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
        DbContext.AddRange(aapl, msft);

        var holders = new InstitutionalHolder[4];
        for (var i = 0; i < holders.Length; i++)
            holders[i] = new InstitutionalHolder { Cik = $"H{i + 1}", Name = $"Filer {i + 1}" };
        DbContext.AddRange(holders.Cast<object>().ToArray());

        // AAPL: held by 2 filers with $1,000,000 each → total $2M, low breadth.
        for (var i = 0; i < 2; i++)
            DbContext.Add(MakeHolding(aapl, holders[i], current, shares: 1_000, value: 1_000_000));
        // MSFT: held by 4 filers with $100,000 each → total $400k, high breadth.
        for (var i = 0; i < 4; i++)
            DbContext.Add(MakeHolding(msft, holders[i], current, shares: 100, value: 100_000));
        // Anchor a prior quarter so ResolveMarketActivityDates finds a comparison.
        DbContext.Add(MakeHolding(aapl, holders[0], prior, shares: 1_000, value: 900_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMostHeldStocks(sort: "value");

        output.Should().Contain("Sorted by: value");
        // Value sort: AAPL ($2M) must precede MSFT ($400k) — opposite of the
        // default filers sort, which would put MSFT (4 filers) first.
        output
            .IndexOf("AAPL", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("MSFT", StringComparison.Ordinal));
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesDbContext ctx) =>
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
