using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Extensions;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins <c>InstitutionalHoldingRepository.GetQuarterlyActivity</c>. The query runs
/// server-side against ParadeDB so the conditional-Sum pattern and the nested
/// <c>Where().Select().Distinct().Count()</c> filer-count must translate end-to-end.
/// Each <see cref="Fact"/> seeds a fresh stock-and-quarter pair so the assertions
/// are isolated from other fixture data.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryQuarterlyActivityTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryQuarterlyActivityTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private Equibles.Data.EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private static readonly DateOnly Prior = new(2024, 9, 30);
    private static readonly DateOnly Current = new(2024, 12, 31);

    [Fact]
    public async Task GetQuarterlyActivity_HolderIncreasedPosition_ProducesPositiveDeltaForStock()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, ticker: "AAPL");
        var holder = await SeedHolder(seed, cik: "1");
        seed.Add(MakeHolding(stock, holder, Prior, shares: 1_000, value: 1_000_000));
        seed.Add(MakeHolding(stock, holder, Current, shares: 1_500, value: 1_650_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var result = await sut.GetQuarterlyActivity(Current, Prior)
            .Where(a => a.CommonStockId == stock.Id)
            .ToListAsync();

        result.Should().ContainSingle();
        var row = result[0];
        row.CurrentShares.Should().Be(1_500);
        row.PreviousShares.Should().Be(1_000);
        row.DeltaShares.Should().Be(500);
        row.DeltaValue.Should().Be(650_000);
        row.CurrentFilerCount.Should().Be(1);
        row.PreviousFilerCount.Should().Be(1);
    }

    [Fact]
    public async Task GetQuarterlyActivity_HolderReducedPosition_ProducesNegativeDeltaForStock()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, ticker: "MSFT");
        var holder = await SeedHolder(seed, cik: "2");
        seed.Add(MakeHolding(stock, holder, Prior, shares: 1_000, value: 1_000_000));
        seed.Add(MakeHolding(stock, holder, Current, shares: 600, value: 660_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetQuarterlyActivity(Current, Prior)
            .SingleAsync(a => a.CommonStockId == stock.Id);

        row.DeltaShares.Should().Be(-400);
        row.DeltaValue.Should().Be(-340_000);
        row.CurrentFilerCount.Should().Be(1);
        row.PreviousFilerCount.Should().Be(1);
    }

    [Fact]
    public async Task GetQuarterlyActivity_NewPositionThisQuarter_PreviousFieldsAreZero()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, ticker: "NVDA");
        var holder = await SeedHolder(seed, cik: "3");
        // No prior-quarter row; current quarter only.
        seed.Add(MakeHolding(stock, holder, Current, shares: 750, value: 825_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetQuarterlyActivity(Current, Prior)
            .SingleAsync(a => a.CommonStockId == stock.Id);

        row.PreviousShares.Should().Be(0);
        row.PreviousValue.Should().Be(0);
        row.CurrentShares.Should().Be(750);
        row.CurrentValue.Should().Be(825_000);
        row.PreviousFilerCount.Should().Be(0);
        row.CurrentFilerCount.Should().Be(1);
    }

    [Fact]
    public async Task GetQuarterlyActivity_SoldOutPosition_CurrentFieldsAreZero()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, ticker: "TSLA");
        EquityIssuer other = await SeedStock(seed, ticker: "KO");
        var holder = await SeedHolder(seed, cik: "4");
        // Prior quarter only; this quarter's 13F holds another stock.
        seed.Add(MakeHolding(stock, holder, Prior, shares: 500, value: 500_000));
        seed.Add(MakeHolding(other, holder, Current, shares: 10, value: 10_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetQuarterlyActivity(Current, Prior)
            .SingleAsync(a => a.CommonStockId == stock.Id);

        row.CurrentShares.Should().Be(0);
        row.CurrentValue.Should().Be(0);
        row.PreviousShares.Should().Be(500);
        row.DeltaShares.Should().Be(-500);
        row.CurrentFilerCount.Should().Be(0);
        row.PreviousFilerCount.Should().Be(1);
    }

    [Fact]
    public async Task GetQuarterlyActivity_MultipleFilersPerStock_CountsDistinctHolders()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, ticker: "GOOG");
        var h1 = await SeedHolder(seed, cik: "5");
        var h2 = await SeedHolder(seed, cik: "6");
        var h3 = await SeedHolder(seed, cik: "7");
        seed.Add(MakeHolding(stock, h1, Prior, shares: 100, value: 100_000));
        seed.Add(MakeHolding(stock, h2, Prior, shares: 200, value: 200_000));
        seed.Add(MakeHolding(stock, h1, Current, shares: 150, value: 150_000));
        seed.Add(MakeHolding(stock, h2, Current, shares: 250, value: 250_000));
        seed.Add(MakeHolding(stock, h3, Current, shares: 50, value: 50_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetQuarterlyActivity(Current, Prior)
            .SingleAsync(a => a.CommonStockId == stock.Id);

        row.PreviousFilerCount.Should().Be(2);
        row.CurrentFilerCount.Should().Be(3);
        row.PreviousShares.Should().Be(300);
        row.CurrentShares.Should().Be(450);
    }

    // Shape of a confidential-treatment original (Norges Bank, 2026-03-31): the filer published a
    // full book the quarter before and after, but its 13F for the middle quarter is one
    // placeholder row, so that quarter holds no row and no rollup for it. Its missing positions
    // are no sale and its returning book is no buy; a first-time filer's book is no buy either.
    [Fact]
    public async Task GetQuarterlyActivity_FilerWithoutA13FForTheOtherQuarter_IsLeftOutOfTheDelta()
    {
        var before = new DateOnly(2025, 12, 31);
        var confidential = new DateOnly(2026, 3, 31);
        var after = new DateOnly(2026, 6, 30);
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, ticker: "AAPL");
        EquityIssuer other = await SeedStock(seed, ticker: "PEP");
        var withheld = await SeedHolder(seed, cik: "1374170");
        var seller = await SeedHolder(seed, cik: "8");
        var firstFiler = await SeedHolder(seed, cik: "9");
        var buyer = await SeedHolder(seed, cik: "10");
        seed.Add(MakeHolding(stock, withheld, before, shares: 1_000_000, value: 100_000_000));
        seed.Add(MakeHolding(stock, withheld, after, shares: 1_000_000, value: 100_000_000));
        seed.Add(MakeHolding(stock, seller, before, shares: 500, value: 50_000));
        seed.Add(MakeHolding(stock, seller, confidential, shares: 200, value: 20_000));
        seed.Add(MakeHolding(stock, firstFiler, confidential, shares: 9_000, value: 900_000));
        seed.Add(MakeHolding(other, buyer, before, shares: 10, value: 1_000));
        seed.Add(MakeHolding(stock, buyer, confidential, shares: 100, value: 10_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var middle = await sut.GetQuarterlyActivity(confidential, before)
            .SingleAsync(a => a.CommonStockId == stock.Id);
        (middle.UnmatchedPreviousShares, middle.UnmatchedCurrentShares)
            .Should()
            .Be((1_000_000L, 9_000L), "the withheld book and the first 13F have no comparison");
        middle.UnmatchedPreviousValue.Should().Be(100_000_000);
        middle.UnmatchedCurrentValue.Should().Be(900_000);
        middle.DeltaShares.Should().Be(-200, "only the seller (-300) and the buyer (+100) traded");
        middle.DeltaValue.Should().Be(-20_000);
        middle.CurrentShares.Should().Be(9_300, "the totals stay as filed");
        middle.PreviousShares.Should().Be(1_000_500);

        var returning = await sut.GetQuarterlyActivity(after, confidential)
            .SingleAsync(a => a.CommonStockId == stock.Id);
        returning.UnmatchedCurrentShares.Should().Be(1_000_000, "the returning book is no buy");
        returning.UnmatchedPreviousShares.Should().Be(9_300, "nobody else filed the last quarter");
        returning.DeltaShares.Should().Be(0);

        var sellers = await sut.GetQuarterlyActivity(confidential, before)
            .TopSellers()
            .Where(a => a.CommonStockId == stock.Id)
            .ToListAsync();
        sellers.Should().ContainSingle("the matched filers sold on balance");
        var buyers = await sut.GetQuarterlyActivity(confidential, before)
            .TopBuyers()
            .Where(a => a.CommonStockId == stock.Id)
            .ToListAsync();
        buyers.Should().BeEmpty("the first-time filer's 9,000 shares are no buy");
    }

    private static async Task<EquityIssuer> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: $"{ticker} Test Corp.",
            Cik: $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}"
        );
        ctx.Add(stock);
        await ctx.SaveChangesAsync();
        return stock;
    }

    private static async Task<InstitutionalHolder> SeedHolder(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string cik
    )
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = $"Holder {cik}" };
        ctx.Add(holder);
        await ctx.SaveChangesAsync();
        return holder;
    }

    private static InstitutionalHolding MakeHolding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
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
