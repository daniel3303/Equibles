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
/// Contract for the per-stock slice of <see cref="HoldingsAggregateRefreshService"/>:
/// <see cref="StockQuarterlyActivity"/> must be computed over Form 13F holdings only.
/// Schedule 13D/G rows carry event-driven report dates that cluster around quarter-ends;
/// counting them would resolve the prior quarter to a sparse non-quarter date and collapse
/// every PreviousFilerCount to zero (#3732).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsAggregateRefreshServiceStockActivityTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public HoldingsAggregateRefreshServiceStockActivityTests(ParadeDbFixture fixture) =>
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

    // The real prior 13F quarter, the quarter under refresh, and a 13D/G event
    // date that falls between them — the date the buggy max(ReportDate) picked.
    private static readonly DateOnly QPrev = new(2024, 9, 30);
    private static readonly DateOnly QCur = new(2024, 12, 31);
    private static readonly DateOnly PollutionDate = new(2024, 12, 18);

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
    public async Task RebuildQuarterAsync_StockActivity_PriorQuarterIsForm13FOnly_Not13DGEventDate()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        var aapl = await SeedStock(seed, "AAPL", industry);
        var holderA = await SeedHolder(seed, "H001");
        var holderB = await SeedHolder(seed, "H002");
        var holderC = await SeedHolder(seed, "H003");
        var holder13D = await SeedHolder(seed, "H004");
        var holder13G = await SeedHolder(seed, "H005");
        seed.AddRange(
            // Prior 13F quarter: two filers.
            MakeHolding(aapl, holderA, QPrev, 100_000, "acc-a-prev"),
            MakeHolding(aapl, holderB, QPrev, 100_000, "acc-b-prev"),
            // Current 13F quarter: holders A and B stay, C is a new filer.
            MakeHolding(aapl, holderA, QCur, 120_000, "acc-a-cur"),
            MakeHolding(aapl, holderB, QCur, 120_000, "acc-b-cur"),
            MakeHolding(aapl, holderC, QCur, 50_000, "acc-c-cur"),
            // A 13D event-date row between the quarters — the date the buggy
            // max(ReportDate < QCur) latched onto, zeroing every prior count.
            MakeHolding(
                aapl,
                holder13D,
                PollutionDate,
                999_000_000,
                "acc-13d",
                filingType: FilingType.Schedule13D
            ),
            // A 13G row landing on the current quarter date itself must not be
            // counted as a 13F filer.
            MakeHolding(
                aapl,
                holder13G,
                QCur,
                500_000,
                "acc-13g",
                filingType: FilingType.Schedule13G
            )
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(QCur, CancellationToken.None);

        await using var read = FreshContext();
        var row = await read.Set<StockQuarterlyActivity>()
            .SingleAsync(s => s.CommonStockId == aapl.Id && s.ReportDate == QCur);

        row.PreviousReportDate.Should()
            .Be(QPrev, "the prior quarter is the latest Form 13F date, not the 13D/G event date");
        row.CurrentFilerCount.Should().Be(3, "holders A, B and C filed 13F this quarter");
        row.PreviousFilerCount.Should()
            .Be(2, "holders A and B filed 13F last quarter — the 13D/G rows are excluded");
        row.NewFilerCount.Should()
            .Be(1, "holder C is the only filer new versus the prior 13F quarter");
        row.SoldOutFilerCount.Should().Be(0, "no 13F filer left between the quarters");
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
            // CUSIP column is varchar(9); a deterministic per-accession value keeps
            // the holding-row unique key disambiguated across the seeds.
            Cusip =
                $"{stock.Ticker[..Math.Min(4, stock.Ticker.Length)]}{accession.GetHashCode():X8}"[
                    ..9
                ],
        };
}
