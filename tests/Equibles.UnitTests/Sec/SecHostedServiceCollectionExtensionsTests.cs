using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Sec;

public class SecHostedServiceCollectionExtensionsTests {
    [Fact]
    public void AddSecWorker_RegistersIDocumentScraperAsScoped() {
        // AddSecWorker is the host's composition entry point for the SEC
        // scraping pipeline. It wires the auto-discovered repositories, the
        // explicit IFilingProcessor / IDocumentPersistenceService /
        // ICompanySyncService / IDocumentScraper bindings, and three
        // BackgroundServices. A regression that drops or downgrades the
        // IDocumentScraper -> DocumentScraper Scoped binding would cause
        // SecScraperWorker to fail resolving its primary collaborator at
        // startup — silent in tests, fatal in production. Pin the binding
        // shape so the regression surfaces here.
        var services = new ServiceCollection();

        services.AddSecWorker();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDocumentScraper));
        descriptor.Should().NotBeNull();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }
}
