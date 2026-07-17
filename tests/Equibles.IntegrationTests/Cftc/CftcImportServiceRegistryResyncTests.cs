using Equibles.Cftc.Data.Models;
using Equibles.Cftc.HostedService.Services;
using Equibles.Cftc.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Cftc.Contracts;
using Equibles.Integrations.Cftc.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Pins the two registry-driven repair behaviors added with the market-code mislabel
/// fix (an earlier <see cref="CuratedContractRegistry"/> revision carried ten wrong
/// display names, e.g. code 057642 said "Lean Hogs" while the CFTC assigns it to
/// Live Cattle):
///
///   1. <c>EnsureContractsExist</c> re-syncs a stored contract's MarketName/Category
///      when they drift from the registry entry for the same code, so deploying a
///      registry correction repairs the production rows without manual SQL.
///   2. <c>DetermineStartYear</c> falls back to a full-history walk while any contract
///      has no reports at all, so a code newly added to the registry backfills its
///      history instead of accumulating only current-year data.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CftcImportServiceRegistryResyncTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public CftcImportServiceRegistryResyncTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

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

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CftcContractRepository))
                    .Returns(new CftcContractRepository(ctx));
                sp.GetService(typeof(CftcPositionReportRepository))
                    .Returns(new CftcPositionReportRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private CftcImportService CreateImporter(
        ICftcClient cftcClient,
        WorkerOptions workerOptions = null
    )
    {
        return new CftcImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<CftcImportService>>(),
            cftcClient,
            Options.Create(workerOptions ?? new WorkerOptions()),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    private static ICftcClient EmptyClient()
    {
        var cftcClient = Substitute.For<ICftcClient>();
        cftcClient.DownloadYearlyReport(Arg.Any<int>()).Returns([]);
        return cftcClient;
    }

    /// <summary>
    /// Seeds every curated contract with registry-true metadata, a LatestReportDate,
    /// and one anchoring position report so DetermineStartYear sees a fully-populated
    /// universe (no empty contract → no full-history walk).
    /// </summary>
    private async Task SeedFullyPopulatedUniverse(DateOnly latestReportDate)
    {
        using var seed = FreshContext();
        CftcContract first = null;
        foreach (var curated in CuratedContractRegistry.Contracts)
        {
            var contract = new CftcContract
            {
                MarketCode = curated.MarketCode,
                MarketName = curated.DisplayName,
                Category = curated.Category,
                LatestReportDate = latestReportDate,
            };
            first ??= contract;
            seed.Set<CftcContract>().Add(contract);
        }
        seed.Set<CftcPositionReport>()
            .Add(
                new CftcPositionReport
                {
                    CftcContract = first,
                    CftcContractId = first.Id,
                    ReportDate = latestReportDate,
                }
            );
        await seed.SaveChangesAsync();
    }

    [Fact]
    public async Task Import_ContractNameDriftedFromRegistry_ResyncsNameAndCategory()
    {
        // A production row created by the mislabeled registry revision: code 057642
        // stored as "Lean Hogs (CME)" although the CFTC assigns that code to Live
        // Cattle. The position reports under the code are Live Cattle's real data,
        // so the repair is a rename of the contract row — Import must apply it.
        using (var seed = FreshContext())
        {
            seed.Set<CftcContract>()
                .Add(
                    new CftcContract
                    {
                        MarketCode = "057642",
                        MarketName = "Lean Hogs (CME)",
                        Category = CftcContractCategory.Agriculture,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var sut = CreateImporter(
            EmptyClient(),
            new WorkerOptions { MinSyncDate = new DateTime(DateTime.UtcNow.Year, 1, 1) }
        );

        await sut.Import(CancellationToken.None);

        using var verify = FreshContext();
        var contract = await verify.Set<CftcContract>().SingleAsync(c => c.MarketCode == "057642");
        contract.MarketName.Should().Be("Live Cattle (CME)");
        contract.Category.Should().Be(CftcContractCategory.Agriculture);
    }

    [Fact]
    public async Task Import_ContractWithNoReports_TriggersFullHistoryWalk()
    {
        // Universe fully populated except one curated code missing entirely — Import
        // creates it (with no reports), which must widen the year walk back to
        // MinSyncDate so the new contract backfills its history. Without the widening,
        // DetermineStartYear would return the global-latest year (2026 here) and the
        // new code would never receive pre-2026 reports.
        var currentYear = DateTime.UtcNow.Year;
        using (var seed = FreshContext())
        {
            CftcContract first = null;
            foreach (var curated in CuratedContractRegistry.Contracts.Skip(1))
            {
                var contract = new CftcContract
                {
                    MarketCode = curated.MarketCode,
                    MarketName = curated.DisplayName,
                    Category = curated.Category,
                    LatestReportDate = new DateOnly(currentYear, 1, 6),
                };
                first ??= contract;
                seed.Set<CftcContract>().Add(contract);
            }
            seed.Set<CftcPositionReport>()
                .Add(
                    new CftcPositionReport
                    {
                        CftcContract = first,
                        CftcContractId = first.Id,
                        ReportDate = new DateOnly(currentYear, 1, 6),
                    }
                );
            await seed.SaveChangesAsync();
        }

        var cftcClient = EmptyClient();
        var sut = CreateImporter(
            cftcClient,
            new WorkerOptions { MinSyncDate = new DateTime(currentYear - 2, 1, 1) }
        );

        await sut.Import(CancellationToken.None);

        await cftcClient.Received(1).DownloadYearlyReport(currentYear - 2);
        await cftcClient.Received(1).DownloadYearlyReport(currentYear - 1);
        await cftcClient.Received(1).DownloadYearlyReport(currentYear);
    }

    [Fact]
    public async Task Import_AllContractsHaveReports_WalksOnlyFromGlobalLatestYear()
    {
        // Control for the full-walk trigger: with every curated contract populated,
        // the incremental behavior must stay put — only the global-latest year is
        // re-walked, not the whole MinSyncDate window.
        var currentYear = DateTime.UtcNow.Year;
        await SeedFullyPopulatedUniverse(new DateOnly(currentYear, 1, 6));

        var cftcClient = EmptyClient();
        var sut = CreateImporter(
            cftcClient,
            new WorkerOptions { MinSyncDate = new DateTime(currentYear - 2, 1, 1) }
        );

        await sut.Import(CancellationToken.None);

        await cftcClient.DidNotReceive().DownloadYearlyReport(currentYear - 1);
        await cftcClient.Received(1).DownloadYearlyReport(currentYear);
    }
}
