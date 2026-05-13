using Equibles.Fred.HostedService.Extensions;
using Equibles.Fred.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Fred;

public class FredServiceCollectionExtensionsTests {
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
