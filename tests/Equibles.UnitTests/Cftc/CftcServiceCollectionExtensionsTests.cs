using Equibles.Cftc.HostedService;
using Equibles.Cftc.HostedService.Extensions;
using Equibles.Cftc.HostedService.Services;
using Equibles.Integrations.Cftc.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Equibles.UnitTests.Cftc;

public class CftcServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCftcWorker_AutoWiresICftcClientFromIntegrationsAssembly()
    {
        // Sibling to `AddCftcWorker_AutoWiresCftcImportService`. The
        // existing pin covers the FIRST `AutoWireServicesFrom` call —
        // the hosted-service assembly scan that registers
        // CftcImportService. AddCftcWorker has a SECOND
        // `AutoWireServicesFrom` call that scans
        // `Equibles.Integrations.Cftc` to wire ICftcClient →
        // CftcClient (the HTTP client behind the COT-report
        // downloader).
        //
        // The two scans are structurally distinct: separate assemblies,
        // and ICftcClient is registered via an interface contract
        // (`[Service(ServiceLifetime.Scoped, typeof(ICftcClient))]`),
        // not via implementation type. The existing CftcImportService
        // pin tests the implementation-is-concrete-class registration
        // pattern; this pin exercises the interface-binding scan.
        //
        // The risk this catches that the CftcImportService sibling
        // cannot:
        //   • A refactor that drops the second AutoWireServicesFrom
        //     (under the false intuition that the import service is
        //     the only thing AddCftcWorker registers) would compile,
        //     pass the existing CftcImportService pin, and silently
        //     leave ICftcClient unresolvable at startup.
        //     CftcScraperWorker's first DoWork iteration would throw
        //     InvalidOperationException from
        //     `GetRequiredService<ICftcClient>()` — the worker logs the
        //     critical exception and the entire COT ingest stalls.
        //   • A wrong-assembly scan (typing
        //     `<Equibles.Integrations.Cboe.CboeClient>` — adjacent
        //     module, easy copy-paste) would also pass the existing
        //     pin but silently register the CBOE client instead.
        //
        // Mirror the existing Cboe pin's pattern: assert presence of
        // the interface descriptor via `services.Should().Contain(d =>
        // d.ServiceType == typeof(ICftcClient))`. ICftcClient is
        // registered via the Service attribute's explicit interface
        // parameter, so the descriptor's ServiceType IS the interface
        // (not the concrete CftcClient).
        var services = new ServiceCollection();

        services.AddCftcWorker();

        services.Should().Contain(d => d.ServiceType == typeof(ICftcClient));
    }

    [Fact]
    public void AddCftcWorker_AutoWiresCftcImportService()
    {
        // AddCftcWorker is the host's seam into auto-wiring for the COT
        // (Commitment of Traders) import pipeline. It scans BOTH the
        // hosted-service assembly AND the Equibles.Integrations.Cftc
        // assembly (for the HTTP client), then adds CftcScraperWorker as
        // a BackgroundService. A regression that drops the
        // AutoWireServicesFrom<CftcImportService> call — or aims at the
        // wrong marker — would silently strip the import service and
        // leave the BackgroundService unable to resolve its primary
        // collaborator. Pin CftcImportService as the canonical
        // scan-was-successful smoke test.
        var services = new ServiceCollection();

        services.AddCftcWorker();

        services.Should().Contain(d => d.ServiceType == typeof(CftcImportService));
    }

    [Fact]
    public void AddCftcWorker_RegistersCftcScraperWorkerAsIHostedService()
    {
        // Third sibling in the AddCftcWorker registration family. The two
        // existing pins cover the AutoWireServicesFrom scans (CftcImportService
        // and ICftcClient). This pin covers the structurally distinct
        // `services.AddHostedService<CftcScraperWorker>()` registration that
        // wires the worker into the .NET generic host so it starts at boot.
        //
        // The risk this catches is asymmetric and unreachable from the two
        // existing AutoWires pins:
        //   • A regression that drops `AddHostedService<CftcScraperWorker>()`
        //     (e.g. a "consolidate worker registrations" refactor that lost
        //     this specific line) would compile cleanly, pass both AutoWires
        //     pins (CftcImportService and ICftcClient are still registered),
        //     and silently disable the entire COT (Commitments of Traders)
        //     ingest pipeline at startup. The application boots, every
        //     dependency resolves, but no IHostedService implementation of
        //     CftcScraperWorker is enumerated — the periodic CFTC COT report
        //     download never fires, position-report data stops updating, and
        //     the CFTC dashboard silently drifts behind.
        //   • A regression that downgrades the registration to AddScoped or
        //     AddSingleton (instead of AddHostedService) would register the
        //     worker as a resolvable service but NOT enumerate it as
        //     IHostedService — same silent failure mode.
        //
        // The COT pipeline is particularly load-bearing because:
        //   • CFTC publishes Commitments of Traders reports weekly (Friday
        //     evenings, covering the prior Tuesday's data). A dropped
        //     registration means the gap from "last successful pull" to
        //     "first noticed missing data" can span multiple weeks before
        //     someone notices the speculator-positioning column has stopped
        //     updating on equity-index/commodities dashboards.
        //   • COT data is the regulator-published signal that drives the
        //     speculator-vs-commercial open-interest splits on the
        //     equity-indices and commodities dashboards — exactly the kind
        //     of metric operators rely on rather than alerting infrastructure
        //     watching.
        //
        // This pin mirrors the AddSecWorker and AddHoldingsWorker
        // hosted-service pin family pattern. CftcScraperWorker is the SINGLE
        // hosted service AddCftcWorker registers — no further siblings in
        // this family.
        //
        // Lookup pattern: filter IHostedService descriptors and assert one has
        // ImplementationType == typeof(CftcScraperWorker).
        var services = new ServiceCollection();

        services.AddCftcWorker();

        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors
            .Should()
            .Contain(
                d => d.ImplementationType == typeof(CftcScraperWorker),
                "AddHostedService<CftcScraperWorker>() must register the worker as IHostedService so the weekly CFTC COT report download runs at startup"
            );
    }
}
