using Equibles.Congress.HostedService.Extensions;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Congress;

public class CongressServiceCollectionExtensionsTests {
    [Fact]
    public void AddCongressWorker_AutoWiresCongressionalTradeSyncService() {
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
}
