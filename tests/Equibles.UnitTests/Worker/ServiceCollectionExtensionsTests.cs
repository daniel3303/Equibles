using Equibles.Worker;
using Equibles.Worker.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Worker;

public class ServiceCollectionExtensionsTests {
    [Fact]
    public void AddWorkerServices_RegistersTickerMapServiceViaAutoWiring() {
        // AddWorkerServices is the host's seam into auto-wiring — it scans
        // the Equibles.Worker assembly and registers every [Service]-
        // attributed type. The composition root (Equibles.Worker.Host)
        // depends on this side-effect: if the scan stops finding services
        // the host boots without ScraperWorker dependencies and silently
        // does no work. TickerMapService carries [Service] and is the
        // canonical worker-assembly type; pin it as the smoke test so a
        // refactor that swaps AutoWireServicesFrom for a different scanner
        // (or accidentally points at the wrong assembly marker) surfaces
        // here.
        var services = new ServiceCollection();

        services.AddWorkerServices();

        services.Should().Contain(d => d.ServiceType == typeof(TickerMapService));
    }
}
