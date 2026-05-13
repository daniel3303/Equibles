using Equibles.Cboe.HostedService.Extensions;
using Equibles.Cboe.HostedService.Services;
using Equibles.Integrations.Cboe.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Cboe;

public class CboeServiceCollectionExtensionsTests {
    [Fact]
    public void AddCboeWorker_AutoWiresCboeImportService() {
        // AddCboeWorker is the host's seam into auto-wiring for the CBOE
        // VIX + put/call-ratio import pipeline. It scans BOTH the
        // hosted-service assembly AND Equibles.Integrations.Cboe (for the
        // HTTP client), then adds CboeScraperWorker as a BackgroundService.
        // A regression that swaps the AutoWireServicesFrom<CboeImportService>
        // marker for a different type — or points at the wrong assembly —
        // would silently strip the import service and leave the
        // BackgroundService unable to resolve its primary collaborator at
        // startup. Pin CboeImportService as the canonical scan-was-
        // successful smoke test.
        var services = new ServiceCollection();

        services.AddCboeWorker();

        services.Should().Contain(d => d.ServiceType == typeof(CboeImportService));
    }

    [Fact]
    public void AddCboeWorker_AutoWiresICboeClientFromIntegrationsAssembly() {
        // Sibling to `AddCboeWorker_AutoWiresCboeImportService`. The existing
        // pin covers the first `AutoWireServicesFrom<CboeImportService>()`
        // call — the hosted-service assembly scan. AddCboeWorker has a
        // SECOND `AutoWireServicesFrom` call that scans the
        // Equibles.Integrations.Cboe assembly to wire ICboeClient →
        // CboeClient (the HTTP client behind the VIX and put/call-ratio
        // downloaders). This second scan is structurally distinct: a
        // separate assembly, a separate auto-wire seam, and the
        // ICboeClient binding (NOT a service-type-matches-impl-type case
        // like CboeImportService).
        //
        // The risk this pin catches that the CboeImportService sibling
        // cannot: a refactor that drops the second AutoWireServicesFrom
        // call (under the false intuition that "CboeImportService is the
        // only service we need to register, the client is implicit") would
        // compile, pass the existing CboeImportService pin, and silently
        // leave ICboeClient unresolvable at startup. CboeScraperWorker's
        // first DoWork iteration would throw InvalidOperationException
        // from `GetRequiredService<ICboeClient>()` — the worker logs the
        // critical exception and the entire CBOE ingest stalls.
        //
        // The complementary risk: a refactor that points the second scan
        // at the WRONG assembly (e.g. typing `<Equibles.Integrations.Cftc.CftcClient>`
        // — adjacent class, easy copy-paste) would also pass the
        // CboeImportService sibling but silently register the CFTC
        // client instead. CboeScraperWorker's GetRequiredService<ICboeClient>
        // would still fail at runtime because the wrong-assembly scan
        // would register ICftcClient, not ICboeClient.
        //
        // Pin: `services.Should().Contain(d => d.ServiceType ==
        // typeof(ICboeClient))`. CboeClient is registered with the
        // `[Service(ServiceLifetime.Scoped, typeof(ICboeClient))]`
        // attribute, so the registered ServiceType IS the interface
        // (not the concrete). Asserting on the interface presence
        // catches all three regression classes above.
        var services = new ServiceCollection();

        services.AddCboeWorker();

        services.Should().Contain(d => d.ServiceType == typeof(ICboeClient));
    }
}
