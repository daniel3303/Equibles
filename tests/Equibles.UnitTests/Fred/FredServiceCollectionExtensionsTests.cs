using Equibles.Fred.HostedService.Extensions;
using Equibles.Fred.HostedService.Services;
using Equibles.Integrations.Fred.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Fred;

public class FredServiceCollectionExtensionsTests {
    [Fact]
    public void AddFredWorker_AutoWiresIFredClientFromIntegrationsAssembly() {
        // Sibling to `AddFredWorker_AutoWiresFredImportService`. The
        // existing pin covers the FIRST `AutoWireServicesFrom` call —
        // the hosted-service assembly scan that registers FredImportService.
        // AddFredWorker has a SECOND `AutoWireServicesFrom` call that
        // scans `Equibles.Integrations.Fred` to wire IFredClient →
        // FredClient (the HTTP client behind api.stlouisfed.org access).
        //
        // The risk this pin catches that the FredImportService sibling
        // cannot:
        //   • A refactor that drops the second AutoWireServicesFrom
        //     would compile, pass the FredImportService sibling, and
        //     silently leave IFredClient unresolvable at startup.
        //     FredScraperWorker.ValidateConfiguration's call into
        //     `IFredClient.IsConfigured` would NRE — the worker's
        //     defensive validation step that the existing
        //     `ValidateConfiguration_FredClientNotConfigured_ReturnsFalse`
        //     pin protects depends on this binding being present.
        //   • A wrong-assembly scan (typo'd as
        //     `<Equibles.Integrations.Finra.FinraClient>` during a
        //     copy-paste refactor) would also pass the FredImportService
        //     sibling but silently register the FINRA client instead.
        //
        // FRED's economic macro series feed (FEDFUNDS, CPIAUCSL, UNRATE,
        // etc.) is the canonical "is the macro dashboard refreshing?"
        // signal. A regression here silently freezes every macro chart
        // on the public site with no exception thrown — the worker logs
        // a critical error and continues, the dashboard reads stale data
        // indefinitely.
        //
        // Mirror the Cboe/Cftc/Yahoo Integrations-assembly-scan pin
        // pattern.
        var services = new ServiceCollection();

        services.AddFredWorker();

        services.Should().Contain(d => d.ServiceType == typeof(IFredClient));
    }

    [Fact]
    public void AddFredWorker_AutoWiresFredImportService() {
        // AddFredWorker is the host's seam into auto-wiring for the FRED
        // macroeconomic series pipeline. It scans BOTH the hosted-service
        // assembly AND Equibles.Integrations.Fred (for the HTTP client),
        // then adds FredScraperWorker as a BackgroundService. A regression
        // that swaps the AutoWireServicesFrom<FredImportService> marker
        // for a different type — or points at the wrong assembly — would
        // silently strip the import service and leave the BackgroundService
        // unable to resolve its primary collaborator at startup. Pin
        // FredImportService as the canonical scan-was-successful smoke test.
        var services = new ServiceCollection();

        services.AddFredWorker();

        services.Should().Contain(d => d.ServiceType == typeof(FredImportService));
    }
}
