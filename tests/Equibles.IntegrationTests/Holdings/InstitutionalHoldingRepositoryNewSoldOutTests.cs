using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins <c>InstitutionalHoldingRepository.GetQuarterlyNewSoldOutPositions</c>. The query
/// uses two NOT-EXISTS subqueries against the same table and MUST translate server-side
/// — the in-memory provider does not exercise the same SQL path, so this fixture runs
/// against ParadeDB.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryNewSoldOutTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryNewSoldOutTests(ParadeDbFixture fixture)
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
    public async Task GetQuarterlyNewSoldOutPositions_HolderAppearsOnlyInCurrent_CountsAsNew()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "AAPL");
        var holder = await SeedHolder(seed, "1");
        seed.Add(MakeHolding(stock, holder, Current, shares: 1_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetQuarterlyNewSoldOutPositions(Current, Prior)
            .SingleAsync(r => r.CommonStockId == stock.Id);

        row.NewFilerCount.Should().Be(1);
        row.SoldOutFilerCount.Should().Be(0);
    }

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_HolderAppearsOnlyInPrior_CountsAsSoldOut()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "MSFT");
        var holder = await SeedHolder(seed, "2");
        seed.Add(MakeHolding(stock, holder, Prior, shares: 1_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetQuarterlyNewSoldOutPositions(Current, Prior)
            .SingleAsync(r => r.CommonStockId == stock.Id);

        row.NewFilerCount.Should().Be(0);
        row.SoldOutFilerCount.Should().Be(1);
    }

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_HolderInBothQuarters_CountsAsNeither()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "NVDA");
        var holder = await SeedHolder(seed, "3");
        seed.Add(MakeHolding(stock, holder, Prior, shares: 1_000));
        seed.Add(MakeHolding(stock, holder, Current, shares: 1_500));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetQuarterlyNewSoldOutPositions(Current, Prior)
            .SingleAsync(r => r.CommonStockId == stock.Id);

        row.NewFilerCount.Should().Be(0);
        row.SoldOutFilerCount.Should().Be(0);
    }

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_MixedFilersPerStock_CountsBothBuckets()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "TSLA");
        var newcomer1 = await SeedHolder(seed, "4");
        var newcomer2 = await SeedHolder(seed, "5");
        var exiter = await SeedHolder(seed, "6");
        var steady = await SeedHolder(seed, "7");
        // Prior: exiter + steady.
        seed.Add(MakeHolding(stock, exiter, Prior, shares: 100));
        seed.Add(MakeHolding(stock, steady, Prior, shares: 200));
        // Current: newcomer1 + newcomer2 + steady (exiter dropped out).
        seed.Add(MakeHolding(stock, newcomer1, Current, shares: 500));
        seed.Add(MakeHolding(stock, newcomer2, Current, shares: 600));
        seed.Add(MakeHolding(stock, steady, Current, shares: 200));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetQuarterlyNewSoldOutPositions(Current, Prior)
            .SingleAsync(r => r.CommonStockId == stock.Id);

        row.NewFilerCount.Should().Be(2);
        row.SoldOutFilerCount.Should().Be(1);
    }

    private static async Task<CommonStock> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Test Corp.",
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
