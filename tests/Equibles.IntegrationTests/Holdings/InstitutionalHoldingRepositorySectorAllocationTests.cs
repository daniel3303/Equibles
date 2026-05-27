using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: GetSectorAllocationByReportDate joins holdings → stocks → industries
/// → sectors and groups by (ReportDate, Sector), summing Value per group.
/// Holdings whose stock has no industry (IndustryId == null) or whose industry
/// has no sector (SectorId == null) are excluded from the result.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositorySectorAllocationTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositorySectorAllocationTests(ParadeDbFixture fixture) =>
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

    // Two stocks in the same sector should produce one snapshot row whose
    // TotalValue is the sum of both holdings. A stock with no industry
    // must be excluded entirely (the join filters it out).
    [Fact]
    public async Task GetSectorAllocationByReportDate_TwoStocksSameSector_SumsValueAndExcludesUnclassified()
    {
        await using var seed = FreshContext();
        var sector = new Sector { Name = "Technology" };
        seed.Add(sector);
        var industry = new Industry
        {
            Name = "Software",
            SectorId = sector.Id,
            Sector = sector,
        };
        seed.Add(industry);
        await seed.SaveChangesAsync();

        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C00000001",
            IndustryId = industry.Id,
        };
        var msft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "C00000002",
            IndustryId = industry.Id,
        };
        var unclassified = new CommonStock
        {
            Ticker = "PRIV",
            Name = "Private Corp.",
            Cik = "C00000003",
        };
        seed.AddRange(aapl, msft, unclassified);

        var holder = new InstitutionalHolder { Cik = "H001", Name = "Fund LP" };
        seed.Add(holder);
        await seed.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 12, 31);
        seed.AddRange(
            MakeHolding(aapl, holder, reportDate, 1_000_000, "acc-aapl"),
            MakeHolding(msft, holder, reportDate, 500_000, "acc-msft"),
            MakeHolding(unclassified, holder, reportDate, 200_000, "acc-priv")
        );
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var snapshots = await sut.GetSectorAllocationByReportDate().ToListAsync();

        snapshots.Should().ContainSingle();
        snapshots[0].ReportDate.Should().Be(reportDate);
        snapshots[0].SectorName.Should().Be("Technology");
        snapshots[0].TotalValue.Should().Be(1_500_000);
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
