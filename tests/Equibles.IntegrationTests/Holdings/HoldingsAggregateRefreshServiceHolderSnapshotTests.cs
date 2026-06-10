using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for the per-holder slice of
/// <see cref="HoldingsAggregateRefreshService"/>: after
/// <c>RebuildQuarterAsync</c>, <see cref="HolderQuarterlySnapshot"/> holds one
/// row per (holder, quarter) whose AUM / position count / stock count /
/// filing date match what the live Form-13F-only aggregate over the same
/// holdings would produce — 13D/G rows excluded, stale holder rows removed.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsAggregateRefreshServiceHolderSnapshotTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public HoldingsAggregateRefreshServiceHolderSnapshotTests(ParadeDbFixture fixture) =>
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

    private static readonly DateOnly Q4 = new(2024, 12, 31);

    private HoldingsAggregateRefreshService BuildService()
    {
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
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    [Fact]
    public async Task RebuildQuarterAsync_AggregatesPerHolder_AumPositionAndStockCounts()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        var aapl = await SeedStock(seed, "AAPL", industry);
        var msft = await SeedStock(seed, "MSFT", industry);
        var holderA = await SeedHolder(seed, "H001");
        var holderB = await SeedHolder(seed, "H002");
        seed.AddRange(
            // Holder A holds AAPL twice (shares + principal rows) and MSFT once:
            // three positions, two distinct stocks.
            MakeHolding(aapl, holderA, Q4, 100_000, "acc-a"),
            MakeHolding(aapl, holderA, Q4, 50_000, "acc-a", shareType: ShareType.Principal),
            MakeHolding(msft, holderA, Q4, 200_000, "acc-a"),
            MakeHolding(msft, holderB, Q4, 700_000, "acc-b")
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(Q4, CancellationToken.None);

        await using var read = FreshContext();
        var a = await read.Set<HolderQuarterlySnapshot>()
            .SingleAsync(s => s.InstitutionalHolderId == holderA.Id && s.ReportDate == Q4);
        a.Aum.Should().Be(350_000);
        a.PositionCount.Should().Be(3);
        a.StockCount.Should().Be(2, "two AAPL rows are still one distinct stock");
        a.FilingDate.Should().Be(Q4.AddDays(45));

        var b = await read.Set<HolderQuarterlySnapshot>()
            .SingleAsync(s => s.InstitutionalHolderId == holderB.Id && s.ReportDate == Q4);
        b.Aum.Should().Be(700_000);
        b.PositionCount.Should().Be(1);
        b.StockCount.Should().Be(1);
    }

    [Fact]
    public async Task RebuildQuarterAsync_Excludes13DGRows_FromHolderAggregates()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        var aapl = await SeedStock(seed, "AAPL", industry);
        var holder13F = await SeedHolder(seed, "H001");
        var holder13D = await SeedHolder(seed, "H002");
        seed.AddRange(
            MakeHolding(aapl, holder13F, Q4, 100_000, "acc-13f"),
            // Same holder also has a 13D event-date row in the quarter — it
            // must not inflate the 13F AUM.
            MakeHolding(
                aapl,
                holder13F,
                Q4,
                999_000_000,
                "acc-13d",
                filingType: FilingType.Schedule13D
            ),
            // A holder with only 13D/G rows must not appear at all.
            MakeHolding(aapl, holder13D, Q4, 500_000, "acc-13g", filingType: FilingType.Schedule13G)
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(Q4, CancellationToken.None);

        await using var read = FreshContext();
        var rows = await read.Set<HolderQuarterlySnapshot>()
            .Where(s => s.ReportDate == Q4)
            .ToListAsync();
        rows.Should().HaveCount(1, "only the 13F filer has a snapshot row");
        rows[0].InstitutionalHolderId.Should().Be(holder13F.Id);
        rows[0].Aum.Should().Be(100_000);
        rows[0].PositionCount.Should().Be(1);
    }

    [Fact]
    public async Task RebuildQuarterAsync_FilingDate_IsLatestAcrossTheQuartersRows()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        var aapl = await SeedStock(seed, "AAPL", industry);
        var msft = await SeedStock(seed, "MSFT", industry);
        var holder = await SeedHolder(seed, "H001");
        var original = MakeHolding(aapl, holder, Q4, 100_000, "acc-orig");
        var amendment = MakeHolding(msft, holder, Q4, 200_000, "acc-amend");
        amendment.FilingDate = Q4.AddDays(90);
        seed.AddRange(original, amendment);
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(Q4, CancellationToken.None);

        await using var read = FreshContext();
        var row = await read.Set<HolderQuarterlySnapshot>()
            .SingleAsync(s => s.InstitutionalHolderId == holder.Id && s.ReportDate == Q4);
        row.FilingDate.Should().Be(Q4.AddDays(90), "the amendment filed later wins");
    }

    [Fact]
    public async Task RebuildQuarterAsync_RemovesStaleHolderRows_AndUpdatesExisting()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        var aapl = await SeedStock(seed, "AAPL", industry);
        var holder = await SeedHolder(seed, "H001");
        var goneHolder = await SeedHolder(seed, "H002");
        seed.Add(MakeHolding(aapl, holder, Q4, 100_000, "acc-q4"));
        // Stale snapshot rows from a previous rebuild: one for a holder whose
        // 13F rows have since been deleted, one for the surviving holder with
        // outdated aggregates.
        seed.AddRange(
            new HolderQuarterlySnapshot
            {
                InstitutionalHolderId = goneHolder.Id,
                ReportDate = Q4,
                FilingDate = Q4.AddDays(45),
                Aum = 42,
                PositionCount = 1,
                StockCount = 1,
            },
            new HolderQuarterlySnapshot
            {
                InstitutionalHolderId = holder.Id,
                ReportDate = Q4,
                FilingDate = Q4.AddDays(1),
                Aum = 1,
                PositionCount = 99,
                StockCount = 99,
            }
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(Q4, CancellationToken.None);

        await using var read = FreshContext();
        var rows = await read.Set<HolderQuarterlySnapshot>()
            .Where(s => s.ReportDate == Q4)
            .ToListAsync();
        rows.Should().HaveCount(1, "the holder with no remaining 13F rows is dropped");
        rows[0].InstitutionalHolderId.Should().Be(holder.Id);
        rows[0].Aum.Should().Be(100_000, "the surviving holder's row is overwritten in place");
        rows[0].PositionCount.Should().Be(1);
    }

    private static async Task<Guid> SeedTaxonomy(Equibles.Data.EquiblesFinancialDbContext ctx)
    {
        var sector = new Sector { Name = "Technology" };
        ctx.Add(sector);
        await ctx.SaveChangesAsync();
        var industry = new Industry { Name = "Software", SectorId = sector.Id };
        ctx.Add(industry);
        await ctx.SaveChangesAsync();
        return industry.Id;
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
        string accession,
        ShareType shareType = ShareType.Shares,
        FilingType filingType = FilingType.Form13F
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 100,
            Value = value,
            ShareType = shareType,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
            FilingType = filingType,
            // CUSIP column is varchar(9). Derive a deterministic 9-char value
            // from the stock so the holding-row unique key stays disambiguated
            // across the test seeds.
            Cusip =
                $"{stock.Ticker[..Math.Min(4, stock.Ticker.Length)]}{stock.Id.GetHashCode():X8}"[
                    ..9
                ],
        };
}
