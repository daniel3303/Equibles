using Equibles.Cftc.Data.Models;
using Equibles.Cftc.HostedService.Services;
using Equibles.Cftc.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Equibles.Integrations.Cftc.Contracts;
using Equibles.Integrations.Cftc.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Exercises the heavy <see cref="CftcImportService.Import"/> pipeline against a real
/// ParadeDB container so the DB-touching phases — <c>EnsureContractsExist</c>,
/// <c>DetermineStartYear</c> (which queries <c>GetGlobalLatestDate</c>), per-year
/// dedup against <c>existingKeys</c>, batch inserts via <c>FlushBatch</c>, and
/// <c>UpdateContractMetadata</c> — all run end-to-end. None of these are reachable
/// from the sibling <see cref="Equibles.UnitTests.Cftc.CftcImportServiceTests"/>
/// file (which only pins the pure-logic <c>ParseDate</c> branches via reflection).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CftcImportServiceFullPipelineTests : IAsyncLifetime {
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public CftcImportServiceFullPipelineTests(ParadeDbFixture fixture) {
        _fixture = fixture;
    }

    public async Task InitializeAsync() {
        await _fixture.ResetAsync();
    }

    public Task DisposeAsync() {
        foreach (var ctx in _contexts) ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesDbContext FreshContext() {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// Builds an <see cref="IServiceScopeFactory"/> whose every <c>CreateScope()</c> call
    /// yields a fresh <see cref="EquiblesDbContext"/> bound to the same ParadeDB instance
    /// — mirroring production DI's scoped-DbContext lifetime. Each repository the
    /// importer pulls out of a scope therefore gets its own context, so saves don't
    /// fight for the same change-tracker.
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory() {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => {
            var ctx = FreshContext();
            var sp = Substitute.For<IServiceProvider>();
            sp.GetService(typeof(CftcContractRepository)).Returns(new CftcContractRepository(ctx));
            sp.GetService(typeof(CftcPositionReportRepository)).Returns(new CftcPositionReportRepository(ctx));
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);
            return scope;
        });
        return scopeFactory;
    }

    private CftcImportService CreateImporter(ICftcClient cftcClient, WorkerOptions workerOptions = null) {
        return new CftcImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<CftcImportService>>(),
            cftcClient,
            Options.Create(workerOptions ?? new WorkerOptions()),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()));
    }

    [Fact]
    public async Task Import_DownloadsCuratedRecords_PersistsContractsAndReportsAndDropsNonCuratedRows() {
        // Pins the full Import pipeline end-to-end:
        //   EnsureContractsExist (auto-insert every curated contract the DB doesn't yet have) →
        //   DetermineStartYear (no global latest date, fall back to MinSyncDate.Year) →
        //   ImportYear (download → filter by curated lookup → dedup by (contractId, date) →
        //              batch-insert via FlushBatch's scoped repository) →
        //   UpdateContractMetadata (LatestReportDate on every contract that received rows).
        //
        // The downloaded record set deliberately mixes THREE conditions in one fixture so
        // a single test failure points at a specific pipeline phase:
        //
        //   • Curated market code "001602" (Wheat-SRW) with modern YYYY-MM-DD date —
        //     proves the curated-lookup admits this row AND ParseDate's first
        //     TryParseExact (yyyy-MM-dd) branch fires.
        //   • Curated market code "067651" (Crude Oil) with legacy YYMMDD date —
        //     proves ParseDate's fallback TryParseExact (yyMMdd) branch fires.
        //     CFTC's pre-2010 history files use this format exclusively; without the
        //     fallback, decades of legacy rows silently disappear.
        //   • Non-curated market code "999999" — proves the curated-lookup FILTER
        //     drops it before BuildPriceMap-style downstream work. A regression that
        //     loosens the filter (e.g., `curatedLookup.ContainsKey(...)` → `true`)
        //     would silently flood the DB with off-target CFTC contracts and break
        //     the curated-only display invariant the MCP / web UIs rely on.
        //
        // The stub returns these records only for `targetYear` so the year loop in
        // Import (which runs from startYear..UtcNow.Year) doesn't double-insert on
        // subsequent iterations. Years other than target return an empty list — also
        // pins the empty-records early-exit in ImportYear (`if (filtered.Count == 0) return`).
        const int targetYear = 2025;
        var records = new List<CftcReportRecord> {
            new() {
                ContractMarketCode = "001602",                  // curated — Wheat-SRW (CBOT)
                ReportDate = "2025-03-04",                      // modern format
                OpenInterest = 500_000,
                NonCommLong = 200_000,
                NonCommShort = 150_000,
                NonCommSpreads = 50_000,
                CommLong = 180_000,
                CommShort = 200_000,
                TotalRptLong = 430_000,
                TotalRptShort = 400_000,
                NonRptLong = 70_000,
                NonRptShort = 100_000,
            },
            new() {
                ContractMarketCode = "067651",                  // curated — Crude Oil, Light Sweet (NYMEX)
                ReportDate = "250311",                          // legacy YYMMDD → 2025-03-11
                OpenInterest = 1_000_000,
                NonCommLong = 400_000,
                NonCommShort = 350_000,
                NonCommSpreads = 100_000,
                CommLong = 350_000,
                CommShort = 400_000,
                TotalRptLong = 850_000,
                TotalRptShort = 850_000,
                NonRptLong = 150_000,
                NonRptShort = 150_000,
            },
            new() {
                ContractMarketCode = "999999",                  // NOT curated — must be filtered out
                ReportDate = "2025-03-04",
                OpenInterest = 1,
            },
        };

        var cftcClient = Substitute.For<ICftcClient>();
        cftcClient.DownloadYearlyReport(Arg.Any<int>())
            .Returns(call => (int)call[0] == targetYear ? records : []);

        var sut = CreateImporter(cftcClient, new WorkerOptions {
            MinSyncDate = new DateTime(targetYear, 1, 1),
        });

        await sut.Import(CancellationToken.None);

        using var verify = FreshContext();

        // EnsureContractsExist seeded every curated contract with its registry metadata.
        var wheat = await verify.Set<CftcContract>().SingleAsync(c => c.MarketCode == "001602");
        var crude = await verify.Set<CftcContract>().SingleAsync(c => c.MarketCode == "067651");
        wheat.MarketName.Should().Be("Wheat-SRW (CBOT)");
        crude.MarketName.Should().Be("Crude Oil, Light Sweet (NYMEX)");

        // Non-curated code did NOT spawn a contract row — the registry is the only source.
        var nonCurated = await verify.Set<CftcContract>().FirstOrDefaultAsync(c => c.MarketCode == "999999");
        nonCurated.Should().BeNull("curated-lookup filter must keep non-registry codes out of CftcContract");

        // Only the two curated rows survived the filter, with dates correctly parsed via BOTH branches.
        var reports = await verify.Set<CftcPositionReport>().ToListAsync();
        reports.Should().HaveCount(2, "the non-curated 999999 row must be dropped before insert");

        var wheatReport = reports.Single(r => r.CftcContractId == wheat.Id);
        wheatReport.ReportDate.Should().Be(new DateOnly(2025, 3, 4));
        wheatReport.OpenInterest.Should().Be(500_000);
        wheatReport.NonCommLong.Should().Be(200_000);

        var crudeReport = reports.Single(r => r.CftcContractId == crude.Id);
        crudeReport.ReportDate.Should().Be(new DateOnly(2025, 3, 11),
            "legacy YYMMDD branch of ParseDate must resolve 250311 → 2025-03-11");
        crudeReport.OpenInterest.Should().Be(1_000_000);

        // UpdateContractMetadata wrote LatestReportDate on every contract that received rows.
        wheat.LatestReportDate.Should().Be(new DateOnly(2025, 3, 4));
        crude.LatestReportDate.Should().Be(new DateOnly(2025, 3, 11));
    }
}
