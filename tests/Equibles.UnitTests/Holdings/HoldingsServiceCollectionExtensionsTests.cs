using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Equibles.UnitTests.Holdings;

public class HoldingsServiceCollectionExtensionsTests {
    [Fact]
    public void AddHoldingsWorker_AutoWiresHoldingsImportService() {
        // AddHoldingsWorker is the host's seam into auto-wiring for the
        // 13F-HR import pipeline. It registers HoldingsImportService (via
        // assembly scan from the [Service] attribute) and the
        // HoldingsScraperWorker BackgroundService. A regression that swaps
        // the AutoWireServicesFrom marker for a different type — or points
        // at the wrong assembly — would silently strip the registrations
        // and leave the BackgroundService unable to resolve its primary
        // collaborator at startup. Pin HoldingsImportService as the
        // canonical scan-was-successful smoke test so the regression
        // surfaces here.
        var services = new ServiceCollection();

        services.AddHoldingsWorker();

        services.Should().Contain(d => d.ServiceType == typeof(HoldingsImportService));
    }

    [Fact]
    public void AddHoldingsWorker_RegistersHoldingsScraperWorkerAsIHostedService() {
        // Sibling to AddHoldingsWorker_AutoWiresHoldingsImportService. The existing
        // pin asserts that the auto-wire scan picked up the import service (the
        // scoped collaborator). This pin asserts a structurally distinct binding:
        // the `services.AddHostedService<HoldingsScraperWorker>()` registration
        // that wires the worker into the .NET generic host so it starts at boot.
        //
        // The risk this catches is asymmetric and unreachable from the existing
        // import-service sibling:
        //   • A regression that drops `AddHostedService<HoldingsScraperWorker>()`
        //     (e.g. a "consolidate worker registrations" refactor that lost this
        //     specific line) would compile cleanly, pass the AutoWires pin
        //     (HoldingsImportService is still registered via the [Service]
        //     attribute scan), and silently disable the entire 13F-HR ingest
        //     pipeline at startup. The application boots, every dependency
        //     resolves, but no IHostedService implementation of
        //     HoldingsScraperWorker is enumerated — the quarterly download of
        //     SEC's 13F structured data sets never runs, institutional holdings
        //     stop updating, and the holdings dashboard silently drifts
        //     behind by a calendar quarter at a time.
        //   • A regression that downgrades the registration to AddScoped or
        //     AddSingleton (instead of AddHostedService) would register the
        //     worker as a resolvable service but NOT enumerate it as
        //     IHostedService — same silent failure mode.
        //
        // The 13F-HR pipeline is particularly load-bearing because:
        //   • Institutional holdings update on a strict quarterly cadence —
        //     the next download window is 6-8 weeks after the last filing
        //     deadline, so the gap from a dropped registration becomes
        //     operator-visible only after a quarter has passed. Far slower
        //     to detect than daily-cadence pipelines (SEC scraper, FTD).
        //   • HoldingsValueRecalculator (separate path) updates pending
        //     prices and would keep operating on the existing data,
        //     producing fresh value updates against stale holdings —
        //     looks healthy in monitoring while silently stale in reality.
        //
        // This pin mirrors the AddSecWorker hosted-service family pattern
        // (3 sibling pins covering SecScraperWorker / DocumentProcessorWorker /
        // FtdScraperWorker IHostedService registrations). HoldingsScraperWorker
        // is the SINGLE hosted service AddHoldingsWorker registers — no further
        // siblings in this family.
        //
        // Lookup pattern: filter IHostedService descriptors and assert one has
        // ImplementationType == typeof(HoldingsScraperWorker).
        var services = new ServiceCollection();

        services.AddHoldingsWorker();

        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors.Should().Contain(
            d => d.ImplementationType == typeof(HoldingsScraperWorker),
            "AddHostedService<HoldingsScraperWorker>() must register the worker as IHostedService so the quarterly 13F-HR import runs at startup");
    }
}
