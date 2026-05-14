using Equibles.Congress.HostedService;
using Equibles.Congress.HostedService.Extensions;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Equibles.UnitTests.Congress;

public class CongressServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCongressWorker_AutoWiresCongressionalTradeSyncService()
    {
        // AddCongressWorker is the host's seam into auto-wiring for the
        // House/Senate disclosure pipeline. It scans the assembly for
        // [Service]-attributed types and adds CongressionalTradeScraperWorker
        // as a BackgroundService. A regression that swaps the
        // AutoWireServicesFrom marker for a different type — or points at
        // the wrong assembly — would silently strip registrations and
        // leave the BackgroundService unable to resolve its primary
        // collaborator at startup. Pin CongressionalTradeSyncService as
        // the canonical scan-was-successful smoke test.
        var services = new ServiceCollection();

        services.AddCongressWorker();

        services.Should().Contain(d => d.ServiceType == typeof(CongressionalTradeSyncService));
    }

    [Fact]
    public void AddCongressWorker_RegistersCongressionalTradeScraperWorkerAsIHostedService()
    {
        // Sibling to AddCongressWorker_AutoWiresCongressionalTradeSyncService.
        // The existing pin asserts the auto-wire scan picked up the sync service
        // (the scoped collaborator). This pin asserts the structurally distinct
        // `services.AddHostedService<CongressionalTradeScraperWorker>()`
        // registration that wires the worker into the .NET generic host so it
        // starts at boot.
        //
        // The risk this catches is asymmetric and unreachable from the existing
        // sync-service sibling:
        //   • A regression that drops `AddHostedService<CongressionalTradeScraperWorker>()`
        //     (e.g. a "consolidate worker registrations" refactor that lost this
        //     specific line) would compile cleanly, pass the AutoWires pin
        //     (CongressionalTradeSyncService is still registered via the [Service]
        //     attribute scan), and silently disable the entire House/Senate
        //     disclosure pipeline at startup. The application boots, every
        //     dependency resolves, but no IHostedService implementation of
        //     CongressionalTradeScraperWorker is enumerated — the periodic
        //     scrape of House PTR PDFs and Senate eFD search results never
        //     fires, congressional-trade data stops updating, and the
        //     "Congress members trading" dashboard silently drifts behind.
        //   • A regression that downgrades the registration to AddScoped or
        //     AddSingleton (instead of AddHostedService) would register the
        //     worker as a resolvable service but NOT enumerate it as
        //     IHostedService — same silent failure mode.
        //
        // The Congress disclosure pipeline is particularly load-bearing because:
        //   • Congressional members file PTRs (Periodic Transaction Reports) on a
        //     45-day delay from the actual trade. A dropped registration means the
        //     gap from "last successful scrape" to "first noticed missing data"
        //     compounds with the existing reporting lag — by the time someone
        //     notices the congressional-trade column has stalled, weeks of
        //     filings may have queued up unscraped.
        //   • Congressional-trade data is the headline feature for many users of
        //     this dataset — pundits, journalists, and retail investors regularly
        //     reference specific members' positioning. A silent stall here is
        //     immediately visible to the dashboard's most-watched audience.
        //
        // This pin mirrors the AddSecWorker, AddHoldingsWorker, and AddCftcWorker
        // hosted-service pin family pattern. CongressionalTradeScraperWorker is
        // the SINGLE hosted service AddCongressWorker registers — no further
        // siblings in this family.
        //
        // Lookup pattern: filter IHostedService descriptors and assert one has
        // ImplementationType == typeof(CongressionalTradeScraperWorker).
        var services = new ServiceCollection();

        services.AddCongressWorker();

        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors
            .Should()
            .Contain(
                d => d.ImplementationType == typeof(CongressionalTradeScraperWorker),
                "AddHostedService<CongressionalTradeScraperWorker>() must register the worker as IHostedService so the House/Senate disclosure scrape runs at startup"
            );
    }
}
