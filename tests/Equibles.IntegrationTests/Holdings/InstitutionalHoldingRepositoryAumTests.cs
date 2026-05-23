using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: GetAumByReportDate groups holdings by ReportDate and returns
/// TotalValue (sum), FilerCount (distinct holders), PositionCount (total rows).
/// FilerCount must deduplicate — a holder with two positions counts once.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryAumTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryAumTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private static readonly DateOnly Q4 = new(2024, 12, 31);

    [Fact]
    public async Task GetAumByReportDate_OneHolderTwoPositions_FilerCountIsOneNotTwo()
    {
        // One holder holding two stocks in the same quarter. FilerCount must
        // count distinct holders (1), not total position rows (2).
        await using var seed = FreshContext();
        var aapl = await SeedStock(seed, "AAPL");
        var msft = await SeedStock(seed, "MSFT");
        var holder = await SeedHolder(seed, "H001");

        seed.AddRange(
            MakeHolding(aapl, holder, Q4, 100_000, "acc-q4-aapl"),
            MakeHolding(msft, holder, Q4, 200_000, "acc-q4-msft")
        );
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var snapshots = await sut.GetAumByReportDate().ToListAsync();

        var q4 = snapshots.Single(s => s.ReportDate == Q4);
        q4.TotalValue.Should().Be(300_000);
        q4.FilerCount.Should().Be(1, "one holder with two positions is still one filer");
        q4.PositionCount.Should().Be(2);
    }

    private static async Task<CommonStock> SeedStock(
        Equibles.Data.EquiblesDbContext ctx,
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
        long value,
        string accession
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
        };
}
