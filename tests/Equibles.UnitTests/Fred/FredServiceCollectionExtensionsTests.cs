using Equibles.Fred.HostedService;
using Equibles.Fred.HostedService.Extensions;
using Equibles.Fred.HostedService.Services;
using Equibles.Integrations.Fred.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Equibles.UnitTests.Fred;

public class FredServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFredWorker_AutoWiresIFredClientFromIntegrationsAssembly()
    {
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
    public void AddFredWorker_AutoWiresFredImportService()
    {
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

    [Fact]
    public void AddFredWorker_RegistersFredScraperWorkerAsIHostedService()
    {
        // Third sibling in the AddFredWorker registration family. The two
        // existing pins cover the AutoWireServicesFrom scans (FredImportService
        // and IFredClient). This pin covers the structurally distinct
        // `services.AddHostedService<FredScraperWorker>()` registration that
        // wires the worker into the .NET generic host so it starts at boot.
        //
        // The risk this catches is asymmetric and unreachable from the two
        // existing AutoWires pins:
        //   • A regression that drops `AddHostedService<FredScraperWorker>()`
        //     would compile cleanly, pass both AutoWires pins (FredImportService
        //     and IFredClient are still registered), and silently disable the
        //     entire FRED macroeconomic series ingest at startup. The application
        //     boots, every dependency resolves, but no IHostedService
        //     implementation of FredScraperWorker is enumerated — the periodic
        //     fetch of FEDFUNDS, CPIAUCSL, UNRATE, and other macro series never
        //     fires. The public dashboard's macro charts silently freeze at
        //     whichever day the regression deployed.
        //   • A regression that downgrades the registration to AddScoped or
        //     AddSingleton would register the worker as a resolvable service
        //     but NOT enumerate it as IHostedService — same silent failure.
        //
        // FRED data updates are daily for the headline series (Fed Funds rate,
        // CPI release windows, jobless claims) and weekly/monthly for the rest.
        // A dropped registration produces a gap that becomes operator-visible
        // on the next data release the public was expecting — but only if
        // someone was watching that specific series. Other series silently
        // accumulate staleness.
        //
        // This pin mirrors the AddSecWorker / AddHoldingsWorker / AddCftcWorker /
        // AddCongressWorker hosted-service pin family pattern. FredScraperWorker
        // is the SINGLE hosted service AddFredWorker registers — no further
        // siblings in this family.
        //
        // Lookup pattern: filter IHostedService descriptors and assert one has
        // ImplementationType == typeof(FredScraperWorker).
        var services = new ServiceCollection();

        services.AddFredWorker();

        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors
            .Should()
            .Contain(
                d => d.ImplementationType == typeof(FredScraperWorker),
                "AddHostedService<FredScraperWorker>() must register the worker as IHostedService so the FRED macroeconomic series fetch runs at startup"
            );
    }
}
