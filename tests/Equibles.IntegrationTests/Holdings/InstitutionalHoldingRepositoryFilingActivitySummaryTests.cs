using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: GetFilingActivitySummary counts distinct filings (by AccessionNumber)
/// and distinct filers (by InstitutionalHolderId) for a stock since a given date.
/// Multiple holding rows under the same accession must count as one filing; the
/// same filer across different filing dates must count as one filer.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryFilingActivitySummaryTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryFilingActivitySummaryTests(ParadeDbFixture fixture) =>
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
    public async Task GetFilingActivitySummary_SameAccessionMultipleRows_CountsOneFilingOneFiler()
    {
        // One filer holds both shares and options of the same stock in a single
        // 13F filing (same AccessionNumber). The summary must deduplicate: 1
        // filing, 1 filer — not 2 of each.
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "AAPL");
        var holder = await SeedHolder(seed, "H001");
        var filingDate = new DateOnly(2025, 2, 14);

        seed.AddRange(
            MakeHolding(stock, holder, filingDate, 100_000, "acc-001", ShareType.Shares),
            MakeHolding(stock, holder, filingDate, 50_000, "acc-001", ShareType.Principal)
        );
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var result = await sut.GetFilingActivitySummary(stock, filingDate.AddDays(-30))
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result.FilingCount.Should().Be(1, "two rows under the same accession are one filing");
        result.FilerCount.Should().Be(1, "same holder in both rows is still one filer");
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
        DateOnly filingDate,
        long value,
        string accession,
        ShareType shareType
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = filingDate,
            ReportDate = new DateOnly(filingDate.Year - 1, 12, 31),
            Shares = value / 100,
            Value = value,
            ShareType = shareType,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
        };
}
