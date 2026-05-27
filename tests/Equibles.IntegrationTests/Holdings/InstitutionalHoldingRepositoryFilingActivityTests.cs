using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: GetFilingActivitySummary counts distinct filings (by AccessionNumber)
/// and distinct filers (by InstitutionalHolderId) for a stock within a date range.
/// Two holdings from the same filer under the same accession must count as one
/// filing and one filer — not two of each.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryFilingActivityTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryFilingActivityTests(ParadeDbFixture fixture) =>
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

    // One filer files one accession covering two positions (AAPL sole + AAPL
    // shared discretion). FilingCount must be 1 (one accession), FilerCount
    // must be 1 (one holder) — not 2 of each.
    [Fact]
    public async Task GetFilingActivitySummary_TwoRowsSameAccession_CountsOneFilingOneFiler()
    {
        await using var seed = FreshContext();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        seed.Add(stock);
        var holder = new InstitutionalHolder { Cik = "H001", Name = "Test Fund LP" };
        seed.Add(holder);
        await seed.SaveChangesAsync();

        var filingDate = new DateOnly(2025, 5, 15);
        seed.AddRange(
            new InstitutionalHolding
            {
                CommonStockId = stock.Id,
                InstitutionalHolderId = holder.Id,
                FilingDate = filingDate,
                ReportDate = new DateOnly(2025, 3, 31),
                Shares = 1_000,
                Value = 100_000,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                AccessionNumber = "0001234567-25-000001",
            },
            new InstitutionalHolding
            {
                CommonStockId = stock.Id,
                InstitutionalHolderId = holder.Id,
                FilingDate = filingDate,
                ReportDate = new DateOnly(2025, 3, 31),
                Shares = 500,
                Value = 50_000,
                ShareType = ShareType.Principal,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                AccessionNumber = "0001234567-25-000001",
            }
        );
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var summary = await sut.GetFilingActivitySummary(stock, filingDate.AddDays(-30))
            .SingleAsync();

        summary.FilingCount.Should().Be(1, "two rows share the same accession number");
        summary.FilerCount.Should().Be(1, "both rows belong to the same holder");
    }
}
