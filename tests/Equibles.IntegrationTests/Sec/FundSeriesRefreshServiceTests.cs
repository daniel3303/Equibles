using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Contract for <see cref="FundSeriesRefreshService"/>: after <c>RebuildAllAsync</c> the
/// <see cref="FundSeries"/> directory holds one row per series taken from that series' latest NPORT
/// report — tracked funds keyed by stock, sweep-discovered trusts keyed by registrant CIK + series
/// id — with the report-header totals, the stored-holdings count, the N-CEN type when on record,
/// and a unique route slug. Superseded reports never linger; rows the run doesn't touch are pruned.
/// Exercised against ParadeDB because the service writes through FlexLabs <c>UpsertRange</c> and
/// <c>ExecuteDeleteAsync</c>, neither of which the in-memory provider supports.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FundSeriesRefreshServiceTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FundSeriesRefreshServiceTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private FundSeriesRefreshService BuildService()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => CreateScopeFromFixture());
        return new FundSeriesRefreshService(
            scopeFactory,
            NullLogger<FundSeriesRefreshService>.Instance
        );
    }

    // Each scope hands out a fresh context plus a repository bound to the same context, mirroring
    // how the worker resolves both from its request scope.
    private IServiceScope CreateScopeFromFixture()
    {
        var ctx = FreshContext();
        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
        provider.GetService(typeof(NportFilingRepository)).Returns(new NportFilingRepository(ctx));
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    [Fact]
    public async Task RebuildAll_MaterialisesTrackedAndTrustSeries_WithIdentityStatsAndSlug()
    {
        await using var seed = FreshContext();
        var cef = await SeedStock(seed, "GAB", "0000038777");
        var trackedFiling = MakeFiling(
            commonStockId: cef.Id,
            registrantCik: null,
            accession: "0000038777-25-000001",
            seriesId: "",
            seriesName: null,
            registrantName: "Gabelli Equity Trust",
            reportPeriod: new DateOnly(2025, 3, 31),
            netAssets: 1_000m,
            totalAssets: 1_100m
        );
        AddHolding(trackedFiling, "037833100", 600m);
        AddHolding(trackedFiling, "594918104", 400m);

        var trustFiling = MakeFiling(
            commonStockId: null,
            registrantCik: "0001100663",
            accession: "0001100663-25-000002",
            seriesId: "S000002277",
            seriesName: "iShares Russell 2000 ETF",
            registrantName: "iShares Trust",
            reportPeriod: new DateOnly(2025, 3, 31),
            netAssets: 2_000m,
            totalAssets: 2_050m
        );
        AddHolding(trustFiling, "037833100", 1_500m);

        seed.AddRange(trackedFiling, trustFiling);
        await seed.SaveChangesAsync();

        await BuildService().RebuildAllAsync(CancellationToken.None);

        await using var read = FreshContext();
        var tracked = await read.Set<FundSeries>().SingleAsync(s => s.CommonStockId == cef.Id);
        tracked.IdentityKey.Should().Be($"cs:{cef.Id}");
        tracked.Ticker.Should().Be("GAB");
        tracked.Slug.Should().Be("gabelli-equity-trust-gab");
        tracked.NetAssets.Should().Be(1_000m);
        tracked.TotalAssets.Should().Be(1_100m);
        tracked.PositionCount.Should().Be(2);
        tracked.LatestReportPeriodDate.Should().Be(new DateOnly(2025, 3, 31));

        var trust = await read.Set<FundSeries>().SingleAsync(s => s.RegistrantCik == "0001100663");
        trust.IdentityKey.Should().Be("rc:0001100663:S000002277");
        trust.SeriesId.Should().Be("S000002277");
        trust.Ticker.Should().BeNull();
        trust.Slug.Should().Be("ishares-russell-2000-etf-s000002277");
        trust.NetAssets.Should().Be(2_000m);
        trust.PositionCount.Should().Be(1, "only the tracked-CUSIP holding is stored for a trust");
    }

    [Fact]
    public async Task RebuildAll_KeepsOnlyTheLatestReportPerSeries()
    {
        await using var seed = FreshContext();
        var cef = await SeedStock(seed, "ECF", "0000123456");
        var older = MakeFiling(
            cef.Id,
            null,
            "0000123456-24-000001",
            "",
            null,
            "Ellsworth Fund",
            new DateOnly(2024, 12, 31),
            netAssets: 500m,
            totalAssets: 550m
        );
        AddHolding(older, "037833100", 500m);
        var newer = MakeFiling(
            cef.Id,
            null,
            "0000123456-25-000002",
            "",
            null,
            "Ellsworth Fund",
            new DateOnly(2025, 3, 31),
            netAssets: 900m,
            totalAssets: 950m
        );
        AddHolding(newer, "037833100", 450m);
        AddHolding(newer, "594918104", 450m);
        seed.AddRange(older, newer);
        await seed.SaveChangesAsync();

        await BuildService().RebuildAllAsync(CancellationToken.None);

        await using var read = FreshContext();
        var row = await read.Set<FundSeries>().SingleAsync(s => s.CommonStockId == cef.Id);
        row.NetAssets.Should().Be(900m, "the newest report wins");
        row.PositionCount.Should().Be(2);
        row.LatestReportPeriodDate.Should().Be(new DateOnly(2025, 3, 31));
    }

    [Fact]
    public async Task RebuildAll_PopulatesFundType_FromLatestNCen()
    {
        await using var seed = FreshContext();
        var cef = await SeedStock(seed, "GAB", "0000038777");
        var filing = MakeFiling(
            cef.Id,
            null,
            "0000038777-25-000001",
            "",
            null,
            "Gabelli Equity Trust",
            new DateOnly(2025, 3, 31),
            netAssets: 1_000m,
            totalAssets: 1_100m
        );
        AddHolding(filing, "037833100", 1_000m);
        seed.Add(filing);
        seed.Add(
            new NCenFiling
            {
                Id = Guid.NewGuid(),
                CommonStockId = cef.Id,
                AccessionNumber = "0000038777-25-000099",
                FilingDate = new DateOnly(2025, 2, 1),
                RegistrantName = "Gabelli Equity Trust",
                InvestmentCompanyType = "N-2",
                ReportEndingPeriod = new DateOnly(2024, 12, 31),
            }
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildAllAsync(CancellationToken.None);

        await using var read = FreshContext();
        var row = await read.Set<FundSeries>().SingleAsync(s => s.CommonStockId == cef.Id);
        row.FundType.Should().Be("N-2");
    }

    [Fact]
    public async Task RebuildAll_PrunesStaleRowsAndUpdatesExistingInPlace()
    {
        await using var seed = FreshContext();
        var cef = await SeedStock(seed, "GAB", "0000038777");
        var filing = MakeFiling(
            cef.Id,
            null,
            "0000038777-25-000001",
            "",
            null,
            "Gabelli Equity Trust",
            new DateOnly(2025, 3, 31),
            netAssets: 1_000m,
            totalAssets: 1_100m
        );
        AddHolding(filing, "037833100", 1_000m);
        seed.Add(filing);
        // A stale directory row no NPORT filing resolves to, plus an outdated row for the series
        // that still exists — the run must drop the first and overwrite the second.
        seed.AddRange(
            new FundSeries
            {
                IdentityKey = "rc:9999999999:S000099999",
                Slug = "ghost-fund-s000099999",
                RegistrantCik = "9999999999",
                SeriesId = "S000099999",
                SeriesName = "Ghost Fund",
                RegistrantName = "Ghost Trust",
                LatestReportPeriodDate = new DateOnly(2020, 1, 1),
                LatestFilingDate = new DateOnly(2020, 1, 15),
                NetAssets = 42m,
                PositionCount = 1,
                ComputedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new FundSeries
            {
                IdentityKey = $"cs:{cef.Id}",
                Slug = "stale-slug",
                CommonStockId = cef.Id,
                SeriesId = "",
                SeriesName = "Gabelli Equity Trust",
                RegistrantName = "Gabelli Equity Trust",
                Ticker = "GAB",
                LatestReportPeriodDate = new DateOnly(2024, 1, 1),
                LatestFilingDate = new DateOnly(2024, 1, 15),
                NetAssets = 1m,
                PositionCount = 99,
                ComputedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildAllAsync(CancellationToken.None);

        await using var read = FreshContext();
        var rows = await read.Set<FundSeries>().ToListAsync();
        rows.Should().ContainSingle("the ghost row with no current filing is pruned");
        rows[0].CommonStockId.Should().Be(cef.Id);
        rows[0].NetAssets.Should().Be(1_000m, "the surviving row is overwritten with fresh stats");
        rows[0].PositionCount.Should().Be(1);
        rows[0].Slug.Should().Be("gabelli-equity-trust-gab");
    }

    private static async Task<CommonStock> SeedStock(
        EquiblesFinancialDbContext ctx,
        string ticker,
        string cik
    )
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = $"{ticker} Fund",
            Cik = cik,
        };
        ctx.Add(stock);
        await ctx.SaveChangesAsync();
        return stock;
    }

    private static NportFiling MakeFiling(
        Guid? commonStockId,
        string registrantCik,
        string accession,
        string seriesId,
        string seriesName,
        string registrantName,
        DateOnly reportPeriod,
        decimal netAssets,
        decimal totalAssets
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = commonStockId,
            RegistrantCik = registrantCik,
            AccessionNumber = accession,
            FilingDate = reportPeriod.AddDays(30),
            RegistrantName = registrantName,
            SeriesName = seriesName,
            SeriesId = seriesId,
            ReportPeriodDate = reportPeriod,
            ReportPeriodEnd = reportPeriod.AddMonths(9),
            TotalAssets = totalAssets,
            NetAssets = netAssets,
        };

    private static void AddHolding(NportFiling filing, string cusip, decimal valueUsd) =>
        filing.Holdings.Add(
            new NportHolding
            {
                Id = Guid.NewGuid(),
                Cusip = cusip,
                Name = cusip,
                Balance = valueUsd,
                Units = "NS",
                Currency = "USD",
                ValueUsd = valueUsd,
                PayoffProfile = "Long",
                AssetCategory = "EC",
                IssuerCategory = "CORP",
            }
        );
}
