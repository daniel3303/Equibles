using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Holdings;

public class HoldingsServiceCollectionExtensionsTests {
    [Fact]
    public void AddHoldingsWorker_AutoWiresHoldingsImportService() {
        // AddHoldingsWorker is the host's seam into auto-wiring for the
        // 13F-HR import pipeline. It registers HoldingsImportService (via
        // assembly scan from the [Service] attribute) and the
        // HoldingsScraperWorker BackgroundService. A regression that swaps
        // the AutoWireServicesFrom marker for a different type — or points
        // at the wrong assembly — would silently strip the registrations
        // and leave the BackgroundService unable to resolve its primary
        // collaborator at startup. Pin HoldingsImportService as the
        // canonical scan-was-successful smoke test so the regression
        // surfaces here.
        var services = new ServiceCollection();

        services.AddHoldingsWorker();

        services.Should().Contain(d => d.ServiceType == typeof(HoldingsImportService));
    }
}
