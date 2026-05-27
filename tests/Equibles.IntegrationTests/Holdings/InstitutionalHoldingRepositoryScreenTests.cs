using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins <c>InstitutionalHoldingRepository.Screen</c>. The query runs server-side
/// against ParadeDB so every filter must translate end-to-end. Each test seeds its
/// own stocks + holders so the assertions don't fight over fixture data.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryScreenTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    private static readonly DateOnly Prior = new(2024, 9, 30);
    private static readonly DateOnly Current = new(2024, 12, 31);

    public InstitutionalHoldingRepositoryScreenTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    [Fact]
    public async Task Screen_NoFilters_ProjectsFilerCountsAndValuesForBothQuarters()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, ticker: "AAPL");
        var h1 = await SeedHolder(seed, cik: "h1");
        var h2 = await SeedHolder(seed, cik: "h2");
        seed.Add(MakeHolding(stock, h1, Prior, shares: 1_000, value: 1_000_000));
        seed.Add(MakeHolding(stock, h2, Prior, shares: 500, value: 500_000));
        seed.Add(MakeHolding(stock, h1, Current, shares: 1_500, value: 1_500_000));
        seed.Add(MakeHolding(stock, h2, Current, shares: 800, value: 800_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.Screen(new ScreenerCriteria(), Current, Prior)
            .SingleAsync(r => r.CommonStockId == stock.Id);

        row.CurrentFilerCount.Should().Be(2);
        row.PreviousFilerCount.Should().Be(2);
        row.DeltaFilerCount.Should().Be(0);
        row.CurrentValue.Should().Be(2_300_000);
        row.PreviousValue.Should().Be(1_500_000);
        row.DeltaValue.Should().Be(800_000);
        row.CurrentShares.Should().Be(2_300);
        row.NewFilerCount.Should().Be(0);
        row.SoldOutFilerCount.Should().Be(0);
        row.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task Screen_NewAndSoldOutCountsReflectPerStockChurn()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, ticker: "NVDA");
        var continuingHolder = await SeedHolder(seed, cik: "cont");
        var initiatingHolder = await SeedHolder(seed, cik: "init");
        var exitingHolder = await SeedHolder(seed, cik: "exit");

        // Continuing holder: holds in both quarters.
        seed.Add(MakeHolding(stock, continuingHolder, Prior, shares: 100, value: 100_000));
        seed.Add(MakeHolding(stock, continuingHolder, Current, shares: 100, value: 100_000));
        // Initiating holder: first appears in Current.
        seed.Add(MakeHolding(stock, initiatingHolder, Current, shares: 200, value: 200_000));
        // Exiting holder: appears in Prior only.
        seed.Add(MakeHolding(stock, exitingHolder, Prior, shares: 50, value: 50_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.Screen(new ScreenerCriteria(), Current, Prior)
            .SingleAsync(r => r.CommonStockId == stock.Id);

        row.NewFilerCount.Should().Be(1);
        row.SoldOutFilerCount.Should().Be(1);
        row.CurrentFilerCount.Should().Be(2);
        row.PreviousFilerCount.Should().Be(2);
    }

    [Fact]
    public async Task Screen_MinFilerCount_FiltersStocksBelowThreshold()
    {
        await using var seed = FreshContext();
        var hot = await SeedStock(seed, ticker: "HOT");
        var cold = await SeedStock(seed, ticker: "COLD");
        var h1 = await SeedHolder(seed, cik: "fa");
        var h2 = await SeedHolder(seed, cik: "fb");
        seed.Add(MakeHolding(hot, h1, Current, shares: 1, value: 1));
        seed.Add(MakeHolding(hot, h2, Current, shares: 1, value: 1));
        seed.Add(MakeHolding(cold, h1, Current, shares: 1, value: 1));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var rows = await sut.Screen(new ScreenerCriteria { MinFilerCount = 2 }, Current, Prior)
            .Where(r => r.Ticker == "HOT" || r.Ticker == "COLD")
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].Ticker.Should().Be("HOT");
    }

    [Fact]
    public async Task Screen_MinDeltaValue_FiltersStocksWithoutGrowth()
    {
        await using var seed = FreshContext();
        var growing = await SeedStock(seed, ticker: "GROW");
        var flat = await SeedStock(seed, ticker: "FLAT");
        var h = await SeedHolder(seed, cik: "gh");
        seed.Add(MakeHolding(growing, h, Prior, shares: 100, value: 100_000));
        seed.Add(MakeHolding(growing, h, Current, shares: 100, value: 500_000));
        seed.Add(MakeHolding(flat, h, Prior, shares: 100, value: 100_000));
        seed.Add(MakeHolding(flat, h, Current, shares: 100, value: 110_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var rows = await sut.Screen(
                new ScreenerCriteria { MinDeltaValue = 100_000 },
                Current,
                Prior
            )
            .Where(r => r.Ticker == "GROW" || r.Ticker == "FLAT")
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].Ticker.Should().Be("GROW");
    }

    [Fact]
    public async Task Screen_MinNewPositions_FiltersStocksWithoutEnoughInitiators()
    {
        await using var seed = FreshContext();
        var trending = await SeedStock(seed, ticker: "TREND");
        var quiet = await SeedStock(seed, ticker: "QUIET");
        var h1 = await SeedHolder(seed, cik: "p1");
        var h2 = await SeedHolder(seed, cik: "p2");
        var h3 = await SeedHolder(seed, cik: "p3");
        // TREND: 2 initiators in Current
        seed.Add(MakeHolding(trending, h1, Current, shares: 10, value: 10));
        seed.Add(MakeHolding(trending, h2, Current, shares: 10, value: 10));
        // QUIET: 1 continuing holder, 0 initiators
        seed.Add(MakeHolding(quiet, h3, Prior, shares: 10, value: 10));
        seed.Add(MakeHolding(quiet, h3, Current, shares: 10, value: 10));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var rows = await sut.Screen(new ScreenerCriteria { MinNewPositions = 2 }, Current, Prior)
            .Where(r => r.Ticker == "TREND" || r.Ticker == "QUIET")
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].Ticker.Should().Be("TREND");
    }

    [Fact]
    public async Task Screen_IndustryIds_FiltersToSelectedIndustriesOnly()
    {
        await using var seed = FreshContext();
        var tech = new Industry { Name = "Software" };
        var energy = new Industry { Name = "Energy" };
        seed.AddRange(tech, energy);
        await seed.SaveChangesAsync();
        var techStock = await SeedStock(seed, ticker: "TECH", industryId: tech.Id);
        var energyStock = await SeedStock(seed, ticker: "OIL", industryId: energy.Id);
        var holder = await SeedHolder(seed, cik: "ind1");
        seed.Add(MakeHolding(techStock, holder, Current, shares: 1, value: 1));
        seed.Add(MakeHolding(energyStock, holder, Current, shares: 1, value: 1));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var rows = await sut.Screen(
                new ScreenerCriteria { IndustryIds = [tech.Id] },
                Current,
                Prior
            )
            .Where(r => r.Ticker == "TECH" || r.Ticker == "OIL")
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].Ticker.Should().Be("TECH");
        rows[0].IndustryName.Should().Be("Software");
    }

    [Fact]
    public async Task Screen_MinPctFloat_FiltersStocksOutsideFloatThresholdAndExcludesUnknownShares()
    {
        await using var seed = FreshContext();
        // Heavily held — 80% of float
        var dense = await SeedStock(seed, ticker: "DENSE", sharesOutStanding: 1_000);
        // Lightly held — 5% of float
        var sparse = await SeedStock(seed, ticker: "SPARSE", sharesOutStanding: 1_000);
        // Unknown float — SharesOutStanding == 0
        var unknown = await SeedStock(seed, ticker: "UNK", sharesOutStanding: 0);
        var holder = await SeedHolder(seed, cik: "pf");
        seed.Add(MakeHolding(dense, holder, Current, shares: 800, value: 800));
        seed.Add(MakeHolding(sparse, holder, Current, shares: 50, value: 50));
        seed.Add(MakeHolding(unknown, holder, Current, shares: 999, value: 999));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var rows = await sut.Screen(new ScreenerCriteria { MinPctFloat = 50.0 }, Current, Prior)
            .Where(r => r.Ticker == "DENSE" || r.Ticker == "SPARSE" || r.Ticker == "UNK")
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].Ticker.Should().Be("DENSE");
        rows[0].PercentOfFloat.Should().BeApproximately(80.0, 0.01);
    }

    private static async Task<CommonStock> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker,
        Guid? industryId = null,
        long sharesOutStanding = 0
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Test Corp.",
            Cik = $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}",
            IndustryId = industryId,
            SharesOutStanding = sharesOutStanding,
        };
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
