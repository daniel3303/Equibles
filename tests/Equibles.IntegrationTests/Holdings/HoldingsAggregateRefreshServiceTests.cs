using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for <see cref="HoldingsAggregateRefreshService"/>: after
/// <c>RebuildQuarterAsync</c>, the per-quarter AUM and sector snapshot rows
/// match what the live multi-distinct aggregate over the same holdings would
/// have produced — distinct filer/stock/filing counts, sector value totals,
/// and stale-sector clean-up.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsAggregateRefreshServiceTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public HoldingsAggregateRefreshServiceTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private static readonly DateOnly Q4 = new(2024, 12, 31);
    private static readonly DateOnly Q3 = new(2024, 9, 30);

    private HoldingsAggregateRefreshService BuildService()
    {
        // The service creates a fresh scope per call, so the substitute hands
        // back a scope whose ServiceProvider resolves a fresh DbContext + the
        // two snapshot repositories — exactly what the production scope would
        // do under MassTransit / the worker host.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => CreateScopeFromFixture());
        return new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );
    }

    private IServiceScope CreateScopeFromFixture()
    {
        var ctx = FreshContext();
        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(Equibles.Data.EquiblesFinancialDbContext)).Returns(ctx);
        provider
            .GetService(typeof(AumQuarterlySnapshotRepository))
            .Returns(_ => new AumQuarterlySnapshotRepository(ctx));
        provider
            .GetService(typeof(SectorQuarterlySnapshotRepository))
            .Returns(_ => new SectorQuarterlySnapshotRepository(ctx));
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    [Fact]
    public async Task RebuildQuarterAsync_OneFilerTwoPositionsOneFiling_FilerAndFilingCountAreOne()
    {
        // Same edge case the legacy HoldingsStatsSeededTests pins: a single
        // holder filed once and holds two stocks. The distinct counts must
        // collapse to one filer / one filing, not two.
        await using var seed = FreshContext();
        var tech = await SeedSector(seed, "Technology");
        var industry = await SeedIndustry(seed, "Software", tech.Id);
        var aapl = await SeedStock(seed, "AAPL", industry.Id);
        var msft = await SeedStock(seed, "MSFT", industry.Id);
        var holder = await SeedHolder(seed, "H001");
        seed.AddRange(
            MakeHolding(aapl, holder, Q4, 100_000, "acc-q4"),
            MakeHolding(msft, holder, Q4, 200_000, "acc-q4")
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(Q4, CancellationToken.None);

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.TotalValue.Should().Be(300_000);
        aum.FilerCount.Should().Be(1, "one holder with two positions is still one filer");
        aum.PositionCount.Should().Be(2);
        aum.StockCount.Should().Be(2);
        aum.FilingCount.Should()
            .Be(1, "two positions filed under one accession is still one filing");
    }

    [Fact]
    public async Task RebuildQuarterAsync_ProducesOneSectorRowPerSector_WithSummedValue()
    {
        await using var seed = FreshContext();
        var tech = await SeedSector(seed, "Technology");
        var energy = await SeedSector(seed, "Energy");
        var techIndustry = await SeedIndustry(seed, "Software", tech.Id);
        var energyIndustry = await SeedIndustry(seed, "Oil & Gas", energy.Id);
        var aapl = await SeedStock(seed, "AAPL", techIndustry.Id);
        var msft = await SeedStock(seed, "MSFT", techIndustry.Id);
        var xom = await SeedStock(seed, "XOM", energyIndustry.Id);
        var holder = await SeedHolder(seed, "H001");
        seed.AddRange(
            MakeHolding(aapl, holder, Q4, 500_000, "acc-q4"),
            MakeHolding(msft, holder, Q4, 400_000, "acc-q4"),
            MakeHolding(xom, holder, Q4, 200_000, "acc-q4")
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(Q4, CancellationToken.None);

        await using var read = FreshContext();
        var rows = await read.Set<SectorQuarterlySnapshot>()
            .Where(s => s.ReportDate == Q4)
            .OrderByDescending(s => s.TotalValue)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].SectorName.Should().Be("Technology");
        rows[0].TotalValue.Should().Be(900_000);
        rows[1].SectorName.Should().Be("Energy");
        rows[1].TotalValue.Should().Be(200_000);
    }

    [Fact]
    public async Task RebuildQuarterAsync_StaleSectorRow_GetsRemoved()
    {
        Guid xomId;

        // First pass: tech and energy both have positions.
        await using (var seed = FreshContext())
        {
            var tech = await SeedSector(seed, "Technology");
            var energy = await SeedSector(seed, "Energy");
            var techIndustry = await SeedIndustry(seed, "Software", tech.Id);
            var energyIndustry = await SeedIndustry(seed, "Oil & Gas", energy.Id);
            var aapl = await SeedStock(seed, "AAPL", techIndustry.Id);
            var xom = await SeedStock(seed, "XOM", energyIndustry.Id);
            xomId = xom.Id;
            var holder = await SeedHolder(seed, "H001");
            seed.AddRange(
                MakeHolding(aapl, holder, Q4, 500_000, "acc-q4"),
                MakeHolding(xom, holder, Q4, 200_000, "acc-q4")
            );
            await seed.SaveChangesAsync();
        }

        await BuildService().RebuildQuarterAsync(Q4, CancellationToken.None);

        // Remove the energy position, leaving only tech for Q4.
        await using (var ctx = FreshContext())
        {
            var energyHolding = await ctx.Set<InstitutionalHolding>()
                .SingleAsync(h => h.CommonStockId == xomId);
            ctx.Remove(energyHolding);
            await ctx.SaveChangesAsync();
        }

        await BuildService().RebuildQuarterAsync(Q4, CancellationToken.None);

        await using var read = FreshContext();
        var rows = await read.Set<SectorQuarterlySnapshot>()
            .Where(s => s.ReportDate == Q4)
            .ToListAsync();

        rows.Should().ContainSingle(r => r.SectorName == "Technology");
        rows.Should()
            .NotContain(
                r => r.SectorName == "Energy",
                "the now-empty sector must be removed on rebuild"
            );
    }

    [Fact]
    public async Task RebuildAllAsync_RebuildsEveryDistinctReportDate()
    {
        await using var seed = FreshContext();
        var tech = await SeedSector(seed, "Technology");
        var industry = await SeedIndustry(seed, "Software", tech.Id);
        var aapl = await SeedStock(seed, "AAPL", industry.Id);
        var holder = await SeedHolder(seed, "H001");
        seed.AddRange(
            MakeHolding(aapl, holder, Q3, 100_000, "acc-q3"),
            MakeHolding(aapl, holder, Q4, 200_000, "acc-q4")
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildAllAsync(CancellationToken.None);

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>()
            .OrderByDescending(s => s.ReportDate)
            .ToListAsync();

        aum.Should().HaveCount(2);
        aum[0].ReportDate.Should().Be(Q4);
        aum[0].TotalValue.Should().Be(200_000);
        aum[1].ReportDate.Should().Be(Q3);
        aum[1].TotalValue.Should().Be(100_000);
    }

    private static async Task<Sector> SeedSector(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string name
    )
    {
        var sector = new Sector { Name = name };
        ctx.Add(sector);
        await ctx.SaveChangesAsync();
        return sector;
    }

    private static async Task<Industry> SeedIndustry(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string name,
        Guid sectorId
    )
    {
        var industry = new Industry { Name = name, SectorId = sectorId };
        ctx.Add(industry);
        await ctx.SaveChangesAsync();
        return industry;
    }

    private static async Task<CommonStock> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker,
        Guid industryId
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Corp.",
            Cik = $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}",
            IndustryId = industryId,
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
            // CUSIP column is varchar(9). Derive a deterministic 9-char value
            // from the stock+holder pair so the holding-row unique key
            // (CommonStock, Holder, ReportDate, ShareType, OptionType,
            // FilingType) stays disambiguated across the test seeds.
            Cusip =
                $"{stock.Ticker[..Math.Min(4, stock.Ticker.Length)]}{stock.Id.GetHashCode():X8}"[
                    ..9
                ],
        };
}
