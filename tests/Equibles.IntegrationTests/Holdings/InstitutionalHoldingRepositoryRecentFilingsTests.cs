using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Adversarial test for <c>GetRecentFilings().IsNewFiler</c> — the NOT EXISTS
/// subquery must correlate correctly through the GroupBy + Join to distinguish
/// first-time filers from returning ones.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryRecentFilingsTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryRecentFilingsTests(ParadeDbFixture fixture)
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

    private static readonly DateOnly Q3 = new(2024, 9, 30);
    private static readonly DateOnly Q4 = new(2024, 12, 31);

    [Fact]
    public async Task GetRecentFilings_FilerWithNoPriorQuarter_FlaggedAsNewFiler()
    {
        // Contract: IsNewFiler is true when no holdings exist for the filer at
        // any ReportDate strictly earlier than this filing's ReportDate. A
        // returning filer (Q3 + Q4) must be false; a first-time filer (Q4 only)
        // must be true. The NOT EXISTS subquery inside the Join result selector
        // must correlate with the correct InstitutionalHolderId — a broken
        // correlation would flag both or neither.
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "AAPL");
        var returningFiler = await SeedHolder(seed, "returning");
        var newFiler = await SeedHolder(seed, "firsttime");

        seed.Add(MakeHolding(stock, returningFiler, Q3, accession: "acc-ret-q3"));
        seed.Add(MakeHolding(stock, returningFiler, Q4, accession: "acc-ret-q4"));
        seed.Add(MakeHolding(stock, newFiler, Q4, accession: "acc-new-q4"));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var filings = await sut.GetRecentFilings().ToListAsync();

        var returningFiling = filings.Single(f => f.FilerCik == "returning" && f.ReportDate == Q4);
        var newFiling = filings.Single(f => f.FilerCik == "firsttime");

        returningFiling.IsNewFiler.Should().BeFalse();
        newFiling.IsNewFiler.Should().BeTrue();
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
        string accession
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = 100,
            Value = 100_000,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
        };
}
