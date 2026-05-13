using Equibles.Cftc.HostedService.Extensions;
using Equibles.Cftc.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Cftc;

public class CftcServiceCollectionExtensionsTests {
    [Fact]
    public void AddCftcWorker_AutoWiresCftcImportService() {
        // AddCftcWorker is the host's seam into auto-wiring for the COT
        // (Commitment of Traders) import pipeline. It scans BOTH the
        // hosted-service assembly AND the Equibles.Integrations.Cftc
        // assembly (for the HTTP client), then adds CftcScraperWorker as
        // a BackgroundService. A regression that drops the
        // AutoWireServicesFrom<CftcImportService> call — or aims at the
        // wrong marker — would silently strip the import service and
        // leave the BackgroundService unable to resolve its primary
        // collaborator. Pin CftcImportService as the canonical
        // scan-was-successful smoke test.
        var services = new ServiceCollection();

        services.AddCftcWorker();

        services.Should().Contain(d => d.ServiceType == typeof(CftcImportService));
    }
}
