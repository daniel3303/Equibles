using Equibles.Cftc.HostedService.Extensions;
using Equibles.Cftc.HostedService.Services;
using Equibles.Integrations.Cftc.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Cftc;

public class CftcServiceCollectionExtensionsTests {
    [Fact]
    public void AddCftcWorker_AutoWiresICftcClientFromIntegrationsAssembly() {
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
    public void AddCftcWorker_AutoWiresCftcImportService() {
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
}
