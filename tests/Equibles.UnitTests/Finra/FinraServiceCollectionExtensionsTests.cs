using Equibles.Finra.HostedService;
using Equibles.Finra.HostedService.Extensions;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Equibles.UnitTests.Finra;

public class FinraServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFinraWorker_AutoWiresIFinraClientFromIntegrationsAssembly()
    {
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
    public void AddFinraWorker_AutoWiresShortVolumeImportService()
    {
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

    [Fact]
    public void AddFinraWorker_RegistersFinraScraperWorkerAsIHostedService()
    {
        // Third sibling in the AddFinraWorker registration family. The two
        // existing pins cover the AutoWireServicesFrom scans
        // (ShortVolumeImportService and IFinraClient). This pin covers the
        // structurally distinct `services.AddHostedService<FinraScraperWorker>()`
        // registration that wires the worker into the .NET generic host so it
        // starts at boot.
        //
        // The risk this catches is asymmetric and unreachable from the two
        // existing AutoWires pins:
        //   • A regression that drops `AddHostedService<FinraScraperWorker>()`
        //     would compile cleanly, pass both AutoWires pins
        //     (ShortVolumeImportService and IFinraClient are still registered),
        //     and silently disable the FINRA short-volume + short-interest
        //     ingest at startup. The application boots, every dependency
        //     resolves, but no IHostedService implementation of
        //     FinraScraperWorker is enumerated — the daily short-volume CSV
        //     pull and weekly short-interest snapshot never fire. The
        //     short-selling dashboard's columns silently stop refreshing.
        //   • A regression that downgrades the registration to AddScoped or
        //     AddSingleton would register the worker as a resolvable service
        //     but NOT enumerate it as IHostedService — same silent failure.
        //
        // The FINRA pipeline is particularly load-bearing because:
        //   • Short-volume data is published daily by FINRA on T+1; a dropped
        //     registration produces an immediately-detectable gap on the next
        //     business day's dashboard refresh — but only if someone is
        //     watching that specific column.
        //   • The OAuth2 token-refresh path (the most fragile integration
        //     in the codebase) is partly tested by the IFinraClient pin's
        //     transitive concerns, but the hosted-service registration is
        //     a structurally separate failure mode.
        //
        // This pin mirrors the AddSecWorker / AddHoldingsWorker / AddCftcWorker /
        // AddCongressWorker / AddFredWorker hosted-service pin family pattern.
        // FinraScraperWorker is the SINGLE hosted service AddFinraWorker
        // registers — no further siblings in this family.
        //
        // Lookup pattern: filter IHostedService descriptors and assert one has
        // ImplementationType == typeof(FinraScraperWorker).
        var services = new ServiceCollection();

        services.AddFinraWorker();

        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors
            .Should()
            .Contain(
                d => d.ImplementationType == typeof(FinraScraperWorker),
                "AddHostedService<FinraScraperWorker>() must register the worker as IHostedService so the daily FINRA short-volume pull runs at startup"
            );
    }
}
