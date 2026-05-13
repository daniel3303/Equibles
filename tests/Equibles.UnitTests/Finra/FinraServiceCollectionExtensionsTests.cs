using Equibles.Finra.HostedService.Extensions;
using Equibles.Finra.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Finra;

public class FinraServiceCollectionExtensionsTests {
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
