using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins <c>InstitutionalHoldingRepository.GetMostHeld</c> + <c>GetUniqueFilerIds</c>
/// — the per-stock breadth aggregate that backs the Most-Held landing page and the
/// MCP tool. Runs server-side against ParadeDB so the conditional-Sum pattern, the
/// nested <c>Where().Select().Distinct().Count()</c> filer count, and the trailing
/// <c>CurrentFilerCount &gt; 0</c> filter must translate end-to-end.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryMostHeldTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryMostHeldTests(ParadeDbFixture fixture)
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

    private Equibles.Data.EquiblesDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private static readonly DateOnly Prior = new(2024, 9, 30);
    private static readonly DateOnly Current = new(2024, 12, 31);

    [Fact]
    public async Task GetMostHeld_RanksByCurrentFilerCount_DescendingByDefault()
    {
        await using var seed = FreshContext();
        var aapl = await SeedStock(seed, "AAPL");
        var msft = await SeedStock(seed, "MSFT");
        var nvda = await SeedStock(seed, "NVDA");
        var holders = new List<InstitutionalHolder>();
        for (var i = 0; i < 5; i++)
            holders.Add(await SeedHolder(seed, cik: $"h{i}"));

        // AAPL: 5 filers, MSFT: 3 filers, NVDA: 1 filer.
        for (var i = 0; i < 5; i++)
            seed.Add(MakeHolding(aapl, holders[i], Current, shares: 100, value: 100_000));
        for (var i = 0; i < 3; i++)
            seed.Add(MakeHolding(msft, holders[i], Current, shares: 50, value: 50_000));
        seed.Add(MakeHolding(nvda, holders[0], Current, shares: 10, value: 10_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var rows = await sut.GetMostHeld(Current, Prior)
            .OrderByDescending(a => a.CurrentFilerCount)
            .ToListAsync();

        rows.Should().HaveCount(3);
        rows[0].CommonStockId.Should().Be(aapl.Id);
        rows[0].CurrentFilerCount.Should().Be(5);
        rows[1].CommonStockId.Should().Be(msft.Id);
        rows[1].CurrentFilerCount.Should().Be(3);
        rows[2].CommonStockId.Should().Be(nvda.Id);
        rows[2].CurrentFilerCount.Should().Be(1);
    }

    [Fact]
    public async Task GetMostHeld_StockSoldOutInCurrentQuarter_ExcludedFromRanking()
    {
        await using var seed = FreshContext();
        var aapl = await SeedStock(seed, "AAPL");
        var tsla = await SeedStock(seed, "TSLA");
        var holder = await SeedHolder(seed, cik: "h1");

        // AAPL stays held in current. TSLA was held last quarter only.
        seed.Add(MakeHolding(aapl, holder, Prior, shares: 100, value: 100_000));
        seed.Add(MakeHolding(aapl, holder, Current, shares: 120, value: 120_000));
        seed.Add(MakeHolding(tsla, holder, Prior, shares: 200, value: 200_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var rows = await sut.GetMostHeld(Current, Prior).ToListAsync();

        rows.Should().HaveCount(1);
        rows[0].CommonStockId.Should().Be(aapl.Id);
        rows.Should().NotContain(r => r.CommonStockId == tsla.Id);
    }

    [Fact]
    public async Task GetMostHeld_StockNewInCurrentQuarter_PreviousFieldsAreZero()
    {
        await using var seed = FreshContext();
        var nvda = await SeedStock(seed, "NVDA");
        var h1 = await SeedHolder(seed, cik: "h1");
        var h2 = await SeedHolder(seed, cik: "h2");

        // Two filers initiated NVDA this quarter; nothing prior.
        seed.Add(MakeHolding(nvda, h1, Current, shares: 100, value: 200_000));
        seed.Add(MakeHolding(nvda, h2, Current, shares: 250, value: 500_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetMostHeld(Current, Prior)
            .SingleAsync(a => a.CommonStockId == nvda.Id);

        row.CurrentFilerCount.Should().Be(2);
        row.PreviousFilerCount.Should().Be(0);
        row.CurrentShares.Should().Be(350);
        row.CurrentValue.Should().Be(700_000);
        row.PreviousShares.Should().Be(0);
        row.PreviousValue.Should().Be(0);
        row.DeltaShares.Should().Be(350);
        row.DeltaValue.Should().Be(700_000);
    }

    [Fact]
    public async Task GetMostHeld_QuarterOverQuarterDelta_ReflectsBothQuarters()
    {
        await using var seed = FreshContext();
        var msft = await SeedStock(seed, "MSFT");
        var h1 = await SeedHolder(seed, cik: "h1");
        var h2 = await SeedHolder(seed, cik: "h2");
        var h3 = await SeedHolder(seed, cik: "h3");

        // MSFT: prior had h1+h2 (2 filers), current has h1+h2+h3 (3 filers, +1).
        seed.Add(MakeHolding(msft, h1, Prior, shares: 100, value: 100_000));
        seed.Add(MakeHolding(msft, h2, Prior, shares: 200, value: 200_000));
        seed.Add(MakeHolding(msft, h1, Current, shares: 150, value: 165_000));
        seed.Add(MakeHolding(msft, h2, Current, shares: 250, value: 275_000));
        seed.Add(MakeHolding(msft, h3, Current, shares: 50, value: 55_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var row = await sut.GetMostHeld(Current, Prior)
            .SingleAsync(a => a.CommonStockId == msft.Id);

        row.PreviousFilerCount.Should().Be(2);
        row.CurrentFilerCount.Should().Be(3);
        row.PreviousValue.Should().Be(300_000);
        row.CurrentValue.Should().Be(495_000);
        row.DeltaValue.Should().Be(195_000);
    }

    [Fact]
    public async Task GetUniqueFilerIds_CountsDistinctHoldersForReportDate()
    {
        await using var seed = FreshContext();
        var aapl = await SeedStock(seed, "AAPL");
        var msft = await SeedStock(seed, "MSFT");
        var h1 = await SeedHolder(seed, cik: "h1");
        var h2 = await SeedHolder(seed, cik: "h2");
        var h3 = await SeedHolder(seed, cik: "h3");

        // h1 + h2 file on both stocks at Current; h3 files only AAPL at Current.
        // Prior includes only h1 — verifies the date filter isolates the snapshot.
        seed.Add(MakeHolding(aapl, h1, Current, shares: 100, value: 100_000));
        seed.Add(MakeHolding(aapl, h2, Current, shares: 100, value: 100_000));
        seed.Add(MakeHolding(aapl, h3, Current, shares: 100, value: 100_000));
        seed.Add(MakeHolding(msft, h1, Current, shares: 100, value: 100_000));
        seed.Add(MakeHolding(msft, h2, Current, shares: 100, value: 100_000));
        seed.Add(MakeHolding(aapl, h1, Prior, shares: 100, value: 100_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var currentCount = await sut.GetUniqueFilerIds(Current).CountAsync();
        var priorCount = await sut.GetUniqueFilerIds(Prior).CountAsync();

        currentCount.Should().Be(3);
        priorCount.Should().Be(1);
    }

    private static async Task<CommonStock> SeedStock(
        Equibles.Data.EquiblesDbContext ctx,
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
        Equibles.Data.EquiblesDbContext ctx,
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
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}-{stock.Ticker}",
        };
}
