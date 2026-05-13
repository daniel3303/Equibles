using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Extensions;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Sec;

public class SecHostedServiceCollectionExtensionsTests {
    [Fact]
    public void AddSecWorker_RegistersIFilingProcessorAsInsiderTradingFilingProcessor() {
        // Sibling to `AddSecWorker_RegistersIDocumentScraperAsScoped`. That
        // pin covers the IDocumentScraper binding shape. This pin covers
        // the IFilingProcessor binding — the strategy-pattern dispatch
        // table that decides which form types the SEC scraper processes
        // beyond raw download/storage.
        //
        // IFilingProcessor is a one-of-many extension point: callers
        // register one or more implementations and DocumentScraper
        // resolves them all via `IEnumerable<IFilingProcessor>`. Today
        // the only registered implementation is
        // InsiderTradingFilingProcessor (handles Form 3/4/5 ownership
        // filings). The implementation_type binding matters: a refactor
        // that swapped the concrete to a stub
        // (`services.AddScoped<IFilingProcessor, StubFilingProcessor>()`
        // — a common copy-paste mistake when adding NEW processors to
        // the list) would compile, pass the IDocumentScraper sibling,
        // and silently disable insider-trading filing processing while
        // looking like a "wiring works" green test.
        //
        // The risk this catches:
        //   • Dropped binding: every Form 4 (insider transactions) would
        //     download but never persist insider transactions. The
        //     "Insider Transactions" page would silently stop receiving
        //     new data.
        //   • Lifetime drift: AddScoped vs AddSingleton/AddTransient
        //     matters because InsiderTradingFilingProcessor depends on
        //     scoped repositories (transaction-per-request semantics).
        //     A singleton lifetime would cache repositories across
        //     requests, breaking EF Core's tracking and producing
        //     unpredictable concurrency errors.
        //   • Wrong concrete type: tests above this pin (the IDocumentScraper
        //     sibling) only assert ServiceType+Lifetime; ImplementationType
        //     can drift without that pin failing. This pin asserts the
        //     IMPLEMENTATION TYPE explicitly.
        //
        // Assert ALL THREE: descriptor exists, ImplementationType is the
        // concrete InsiderTradingFilingProcessor, Lifetime is Scoped.
        var services = new ServiceCollection();

        services.AddSecWorker();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IFilingProcessor));
        descriptor.Should().NotBeNull();
        descriptor.ImplementationType.Should().Be(typeof(InsiderTradingFilingProcessor));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

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
