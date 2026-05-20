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
/// Pins <c>GetMarketWide13FActivity</c>. The tool dispatches by `bucket` argument over
/// the same `InstitutionalHoldingRepository.GetQuarterlyActivity` /
/// `GetQuarterlyNewSoldOutPositions` queries used by the web page; each `Fact` exercises
/// one bucket to keep its assertions narrow.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetMarketWide13FActivityTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetMarketWide13FActivityTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetMarketWide13FActivity_UnknownBucket_ReportsValidValues()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMarketWide13FActivity(bucket: "garbage");

        output.Should().Contain("Unknown bucket");
        output.Should().Contain("top-buys");
        output.Should().Contain("sold-out-positions");
    }

    [Fact]
    public async Task GetMarketWide13FActivity_NoData_ReportsNoHoldings()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMarketWide13FActivity(bucket: "top-buys");

        output.Should().Contain("No 13F holdings data");
    }

    [Fact]
    public async Task GetMarketWide13FActivity_TopBuysBucket_RanksByDeltaValueDescending()
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
        var holder = new InstitutionalHolder { Cik = "1", Name = "Big Fund" };
        DbContext.AddRange(aapl, msft, holder);
        // AAPL: Δ +500_000 value
        DbContext.Add(MakeHolding(aapl, holder, prior, shares: 1_000, value: 1_000_000));
        DbContext.Add(MakeHolding(aapl, holder, current, shares: 1_500, value: 1_500_000));
        // MSFT: Δ +200_000 value
        DbContext.Add(MakeHolding(msft, holder, prior, shares: 500, value: 500_000));
        DbContext.Add(MakeHolding(msft, holder, current, shares: 700, value: 700_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMarketWide13FActivity(bucket: "top-buys");

        output.Should().Contain("Market-wide 13F **top-buys**");
        output.Should().Contain("for 2024-12-31");
        output
            .IndexOf("AAPL", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("MSFT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetMarketWide13FActivity_NewPositionsBucket_RanksByInitiatedCount()
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
        // AAPL gets 3 new filers (none in prior), MSFT gets 1 new filer.
        for (var i = 0; i < 3; i++)
        {
            var h = new InstitutionalHolder { Cik = $"newAAPL{i}", Name = $"New AAPL {i}" };
            DbContext.Add(h);
            DbContext.Add(MakeHolding(aapl, h, current, shares: 100, value: 10_000));
        }
        var msftNew = new InstitutionalHolder { Cik = "newMSFT", Name = "New MSFT" };
        DbContext.Add(msftNew);
        DbContext.Add(MakeHolding(msft, msftNew, current, shares: 100, value: 10_000));
        // Seed at least one prior-quarter row anywhere so the prior date exists in distinct dates.
        var anchor = new InstitutionalHolder { Cik = "anchor", Name = "Anchor" };
        DbContext.Add(anchor);
        DbContext.Add(MakeHolding(aapl, anchor, prior, shares: 1, value: 100));
        DbContext.Add(MakeHolding(aapl, anchor, current, shares: 1, value: 100));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetMarketWide13FActivity(bucket: "new-positions");

        output.Should().Contain("new-positions");
        output.Should().Contain("# Filers Initiated");
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
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
