using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Sibling pin to
/// <c>Screen_MinPctFloat_FiltersStocksOutsideFloatThresholdAndExcludesUnknownShares</c>.
/// That test pins the lower-bound branch of the % float filter (and its
/// <c>SharesOutStanding &gt; 0</c> guard). The upper-bound branch carries the same
/// guard and the same code-commented "exclude unknown shares" contract but is
/// unpinned. Without this pin, a refactor that drops the <c>SharesOutStanding &gt; 0</c>
/// predicate from the <c>MaxPctFloat</c> arm would compile cleanly, pass every
/// existing screener test, and throw a PostgreSQL "division by zero" error at
/// query time on any data set that contains a stock with unknown shares.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryScreenMaxPctFloatTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    private static readonly DateOnly Prior = new(2024, 9, 30);
    private static readonly DateOnly Current = new(2024, 12, 31);

    public InstitutionalHoldingRepositoryScreenMaxPctFloatTests(ParadeDbFixture fixture) =>
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

    [Fact]
    public async Task Screen_MaxPctFloat_FiltersStocksAboveThresholdAndExcludesUnknownShares()
    {
        await using var seed = FreshContext();
        // DENSE — 80% of float, must be excluded by MaxPctFloat=50.
        var dense = await SeedStock(seed, ticker: "DENSE", sharesOutStanding: 1_000);
        // SPARSE — 5% of float, must survive.
        var sparse = await SeedStock(seed, ticker: "SPARSE", sharesOutStanding: 1_000);
        // UNK — SharesOutStanding == 0 (unknown). Without the divide-by-zero guard
        // the Postgres query would throw rather than excluding this stock.
        var unknown = await SeedStock(seed, ticker: "UNK", sharesOutStanding: 0);
        var holder = await SeedHolder(seed, cik: "mp");
        seed.Add(MakeHolding(dense, holder, Current, shares: 800, value: 800));
        seed.Add(MakeHolding(sparse, holder, Current, shares: 50, value: 50));
        seed.Add(MakeHolding(unknown, holder, Current, shares: 999, value: 999));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var rows = await sut.Screen(new ScreenerCriteria { MaxPctFloat = 50.0 }, Current, Prior)
            .Where(r => r.Ticker == "DENSE" || r.Ticker == "SPARSE" || r.Ticker == "UNK")
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].Ticker.Should().Be("SPARSE");
        rows[0].PercentOfFloat.Should().BeApproximately(5.0, 0.01);
    }

    private static async Task<CommonStock> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker,
        long sharesOutStanding = 0
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Test Corp.",
            Cik = $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}",
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
