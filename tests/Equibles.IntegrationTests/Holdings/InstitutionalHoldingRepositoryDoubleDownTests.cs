using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: GetDoubleDownPositions returns holders who increased shares ≥
/// minPctIncrease between two quarters. Holders who decreased, stayed flat,
/// or had no prior position must be excluded.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryDoubleDownTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryDoubleDownTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

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

    private static readonly DateOnly Q3 = new(2024, 9, 30);
    private static readonly DateOnly Q4 = new(2024, 12, 31);

    [Fact]
    public async Task GetDoubleDownPositions_MixedChanges_OnlyReturnsIncreasesAboveThreshold()
    {
        // Seed three holders with different Q3→Q4 share changes:
        //   doubler:  100 → 250 shares (+150%)  — above 50% threshold
        //   modest:   100 → 120 shares (+20%)   — below 50% threshold
        //   reducer:  100 → 50 shares  (−50%)   — decrease, excluded
        // Also seed a new-position holder with Q4 only (no Q3) — excluded.
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "AAPL");
        var doubler = await SeedHolder(seed, "doubler");
        var modest = await SeedHolder(seed, "modest");
        var reducer = await SeedHolder(seed, "reducer");
        var newPos = await SeedHolder(seed, "newpos");

        seed.AddRange(
            MakeHolding(stock, doubler, Q3, 100, "acc-doubler-q3"),
            MakeHolding(stock, doubler, Q4, 250, "acc-doubler-q4"),
            MakeHolding(stock, modest, Q3, 100, "acc-modest-q3"),
            MakeHolding(stock, modest, Q4, 120, "acc-modest-q4"),
            MakeHolding(stock, reducer, Q3, 100, "acc-reducer-q3"),
            MakeHolding(stock, reducer, Q4, 50, "acc-reducer-q4"),
            MakeHolding(stock, newPos, Q4, 500, "acc-newpos-q4")
        );
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var positions = await sut.GetDoubleDownPositions(Q4, Q3, 50.0).ToListAsync();

        positions.Should().ContainSingle("only the doubler exceeds 50%");
        positions[0].FilerCik.Should().Be("doubler");
        positions[0].CurrentShares.Should().Be(250);
        positions[0].PreviousShares.Should().Be(100);
        positions[0].Ticker.Should().Be("AAPL");
    }

    private static async Task<CommonStock> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Corp.",
            Cik = $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}",
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
        string accession
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
            AccessionNumber = accession,
        };
}
