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
/// Contract for the open-window combined lane of
/// <see cref="HoldingsAggregateRefreshService"/>: while the newest quarter's 45-day
/// filing window is open, <see cref="StockQuarterlyActivityCombined"/> must carry
/// non-filers forward at their prior-quarter positions (a fund that has not filed yet
/// is assumed to still hold), count only genuinely-filed initiations/exits, and be
/// retired outright once the window closes. Window state is pinned by seeding report
/// dates relative to today — inside the 45-day deadline for the open cases, far past
/// it for the closed case (CombinedQuarterHelper owns the rule).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsAggregateRefreshServiceCombinedLaneTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public HoldingsAggregateRefreshServiceCombinedLaneTests(ParadeDbFixture fixture) =>
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

    // Open-window pair: the "quarter end" sits 20 days back, inside the 45-day window.
    private static readonly DateOnly OpenCur = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);
    private static readonly DateOnly OpenPrev = OpenCur.AddDays(-92);
    private static readonly DateOnly OpenOld = OpenCur.AddDays(-184);

    // Closed-window quarter: 100 days back, past the 45-day deadline.
    private static readonly DateOnly ClosedCur = DateOnly
        .FromDateTime(DateTime.UtcNow)
        .AddDays(-100);

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
    public async Task RebuildQuarterAsync_WindowOpen_CarriesNonFilersForwardAndCountsRealChurn()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        var aapl = await SeedStock(seed, "AAPL", industry);
        var msft = await SeedStock(seed, "MSFT", industry);
        var holderA = await SeedHolder(seed, "H001"); // filed both quarters
        var holderB = await SeedHolder(seed, "H002"); // has NOT filed the open quarter → carried forward
        var holderC = await SeedHolder(seed, "H003"); // new filer this quarter
        var holderD = await SeedHolder(seed, "H004"); // filed the open quarter but dropped AAPL
        seed.AddRange(
            MakeHolding(aapl, holderA, OpenPrev, 100_000, "acc-a-prev"),
            MakeHolding(aapl, holderB, OpenPrev, 50_000, "acc-b-prev"),
            MakeHolding(aapl, holderD, OpenPrev, 10_000, "acc-d-prev"),
            MakeHolding(aapl, holderA, OpenCur, 120_000, "acc-a-cur"),
            MakeHolding(aapl, holderC, OpenCur, 30_000, "acc-c-cur"),
            // D's open-quarter filing holds only MSFT — proof D filed, so no AAPL
            // carry-forward, and D counts as a genuine AAPL exit.
            MakeHolding(msft, holderD, OpenCur, 40_000, "acc-d-cur")
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);

        await using var read = FreshContext();
        var row = await read.Set<StockQuarterlyActivityCombined>()
            .SingleAsync(s => s.CommonStockId == aapl.Id && s.ReportDate == OpenCur);

        row.PreviousReportDate.Should().Be(OpenPrev);
        // MakeHolding stores Shares = value / 100.
        row.CurrentShares.Should()
            .Be(2_000, "A's 1200 + C's 300 + B's 500 carried forward; D filed, so no carry");
        row.PreviousShares.Should().Be(1_600, "A 1000 + B 500 + D 100 last quarter");
        row.CurrentValue.Should().Be(200_000);
        row.PreviousValue.Should().Be(160_000);
        row.CurrentFilerCount.Should().Be(3, "A and C filed, B is carried forward");
        row.PreviousFilerCount.Should().Be(3);
        row.NewFilerCount.Should().Be(1, "only C initiated");
        row.SoldOutFilerCount.Should()
            .Be(1, "D filed this quarter without AAPL — a proven exit; B is assumed to hold");
    }

    [Fact]
    public async Task RebuildQuarterAsync_WindowClosed_RetiresTheCombinedLane()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        var aapl = await SeedStock(seed, "AAPL", industry);
        var holderA = await SeedHolder(seed, "H001");
        seed.AddRange(
            MakeHolding(aapl, holderA, ClosedCur.AddDays(-92), 100_000, "acc-a-prev"),
            MakeHolding(aapl, holderA, ClosedCur, 120_000, "acc-a-cur"),
            // A stale combined row from when the window was open — must not survive
            // a rebuild after the window closed.
            new StockQuarterlyActivityCombined
            {
                CommonStockId = aapl.Id,
                ReportDate = ClosedCur,
                PreviousReportDate = ClosedCur.AddDays(-92),
                CurrentShares = 1,
                ComputedAt = DateTime.UtcNow,
            }
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(ClosedCur, CancellationToken.None);

        await using var read = FreshContext();
        (await read.Set<StockQuarterlyActivityCombined>().AnyAsync())
            .Should()
            .BeFalse("the closed quarter's plain snapshot is authoritative");
    }

    [Fact]
    public async Task RebuildQuarterAsync_HistoricalQuarter_DoesNotTouchTheCombinedLane()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        var aapl = await SeedStock(seed, "AAPL", industry);
        var holderA = await SeedHolder(seed, "H001");
        seed.AddRange(
            MakeHolding(aapl, holderA, OpenOld, 80_000, "acc-a-old"),
            MakeHolding(aapl, holderA, OpenPrev, 100_000, "acc-a-prev"),
            MakeHolding(aapl, holderA, OpenCur, 120_000, "acc-a-cur"),
            // Marker row: a historical quarter's rebuild must neither refresh nor
            // clean the combined lane — only the open/prior quarters' rebuilds (or a
            // closed window) may touch it.
            new StockQuarterlyActivityCombined
            {
                CommonStockId = aapl.Id,
                ReportDate = OpenOld,
                PreviousReportDate = OpenOld.AddDays(-92),
                CurrentShares = 42,
                ComputedAt = DateTime.UtcNow,
            }
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(OpenOld, CancellationToken.None);

        await using var read = FreshContext();
        var marker = await read.Set<StockQuarterlyActivityCombined>().SingleAsync();
        marker.CurrentShares.Should().Be(42, "the historical rebuild skipped the lane");

        // The open quarter's own rebuild then refreshes the lane and sweeps the
        // stale off-quarter marker in the same pass.
        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);
        await using var read2 = FreshContext();
        var rows = await read2.Set<StockQuarterlyActivityCombined>().ToListAsync();
        rows.Should().OnlyContain(r => r.ReportDate == OpenCur);
        rows.Should().ContainSingle(r => r.CommonStockId == aapl.Id);
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
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
            FilingType = filingType,
            Cusip =
                $"{stock.Ticker[..Math.Min(4, stock.Ticker.Length)]}{accession.GetHashCode():X8}"[
                    ..9
                ],
        };
}
