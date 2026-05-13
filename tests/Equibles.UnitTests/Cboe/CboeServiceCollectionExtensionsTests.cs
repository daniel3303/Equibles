using Equibles.Cboe.HostedService.Extensions;
using Equibles.Cboe.HostedService.Services;
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
}
