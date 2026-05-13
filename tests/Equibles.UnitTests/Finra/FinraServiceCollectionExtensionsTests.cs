using Equibles.Finra.HostedService.Extensions;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Finra;

public class FinraServiceCollectionExtensionsTests {
    [Fact]
    public void AddFinraWorker_AutoWiresIFinraClientFromIntegrationsAssembly() {
        // Sibling to `AddFinraWorker_AutoWiresShortVolumeImportService`.
        // The existing pin covers the FIRST `AutoWireServicesFrom` call
        // — the hosted-service assembly scan that registers
        // ShortVolumeImportService. AddFinraWorker has a SECOND
        // `AutoWireServicesFrom` call that scans
        // `Equibles.Integrations.Finra` to wire IFinraClient →
        // FinraClient (the OAuth2-authenticated HTTP client behind
        // FINRA's short-volume + short-interest downloads).
        //
        // The two scans are structurally distinct: separate assemblies,
        // and IFinraClient is registered via interface contract
        // (`[Service(ServiceLifetime.Scoped, typeof(IFinraClient))]`),
        // NOT via the implementation-type-equals-service-type pattern.
        // The existing ShortVolumeImportService pin tests the latter;
        // this pin exercises the interface-binding scan.
        //
        // The risk this catches that the ShortVolumeImportService
        // sibling cannot:
        //   • A refactor that drops the second AutoWireServicesFrom
        //     (under the false intuition that "the import service is
        //     all we need") would compile, pass the existing
        //     ShortVolumeImportService pin, and silently leave
        //     IFinraClient unresolvable at startup. FinraScraperWorker's
        //     ValidateConfiguration's call into IFinraClient.IsConfigured
        //     would NRE — the defensive validation step pinned by the
        //     existing `ValidateConfiguration_FinraClientNotConfigured`
        //     test depends on this binding.
        //   • A wrong-assembly scan (typing
        //     `<Equibles.Integrations.Fred.FredClient>` during a
        //     copy-paste refactor) would also pass the
        //     ShortVolumeImportService sibling but silently register
        //     the FRED client instead.
        //
        // FINRA's OAuth2 flow is the most fragile of the integration
        // clients (token refresh, scope handling, multi-step auth) —
        // a regression here breaks the daily short-volume ingest with
        // no obvious error trail (the worker logs and continues, no
        // 401 surfaces to the operator dashboard).
        //
        // Mirror the Cboe/Cftc/Yahoo/Fred Integrations-assembly-scan
        // pin pattern.
        var services = new ServiceCollection();

        services.AddFinraWorker();

        services.Should().Contain(d => d.ServiceType == typeof(IFinraClient));
    }

    [Fact]
    public void AddFinraWorker_AutoWiresShortVolumeImportService() {
        // AddFinraWorker is the host's seam into auto-wiring for the
        // FINRA short-volume + short-interest pipeline. It scans BOTH
        // the hosted-service assembly AND Equibles.Integrations.Finra
        // (for the OAuth2 HTTP client), then adds FinraScraperWorker as
        // a BackgroundService. A regression that swaps the
        // AutoWireServicesFrom<ShortVolumeImportService> marker for a
        // different type — or points at the wrong assembly — would
        // silently strip the import service and leave the BackgroundService
        // unable to resolve its primary collaborator at startup. Pin
        // ShortVolumeImportService as the canonical scan-was-successful
        // smoke test.
        var services = new ServiceCollection();

        services.AddFinraWorker();

        services.Should().Contain(d => d.ServiceType == typeof(ShortVolumeImportService));
    }
}
