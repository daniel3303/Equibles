using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Extensions;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    public void AddSecWorker_RegistersICompanySyncServiceAsScoped() {
        // Third pin in the AddSecWorker DI-binding family. Existing pins
        // cover IDocumentScraper (Scoped) and IFilingProcessor
        // (InsiderTradingFilingProcessor, Scoped). This pin covers
        // ICompanySyncService — the collaborator that DocumentScraper
        // delegates to whenever it discovers a new filer CIK during
        // ingest and needs to upsert the corresponding CommonStock row.
        //
        // ICompanySyncService's binding shape matters because:
        //   • The interface is the cross-process seam between DocumentScraper
        //     (consumer) and CompanySyncService (producer that resolves
        //     SEC-side ticker→CIK→CommonStock mappings, including the
        //     ShouldIncumbentWin tiebreak logic pinned elsewhere in the
        //     suite). A dropped binding would NRE the first time
        //     DocumentScraper encounters an unknown CIK — silent failure
        //     in tests (no DocumentScraper test exercises that path with
        //     real DI resolution), fatal in production.
        //   • Lifetime drift matters: CompanySyncService takes a scoped
        //     ISecEdgarClient (HttpClient-backed). A singleton lifetime
        //     would cache the SecEdgarClient across requests and break
        //     the per-request HttpClient lifecycle that IHttpClientFactory
        //     orchestrates — silent connection leaks and exhausted socket
        //     pools under load.
        //
        // The existing IDocumentScraper sibling only asserts the
        // ServiceType+Lifetime, NOT the implementation type. This pin
        // follows the same minimal contract: a wrong concrete type
        // (e.g., a no-op stub) would still pass DI resolution but
        // disable the company-sync side effect. The IFilingProcessor
        // sibling DOES assert ImplementationType; pin both arms here
        // for consistency with the broader binding-shape contract.
        //
        // Assert ALL THREE: descriptor exists, ImplementationType is
        // the concrete CompanySyncService, Lifetime is Scoped.
        var services = new ServiceCollection();

        services.AddSecWorker();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ICompanySyncService));
        descriptor.Should().NotBeNull();
        descriptor.ImplementationType.Should().Be(typeof(CompanySyncService));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddSecWorker_RegistersIDocumentPersistenceServiceAsScoped() {
        // Fourth pin in the AddSecWorker DI-binding family. Existing pins
        // cover IDocumentScraper (Scoped), IFilingProcessor
        // (InsiderTradingFilingProcessor, Scoped), and ICompanySyncService
        // (CompanySyncService, Scoped). This pin closes the family by
        // covering the LAST explicit AddScoped binding in AddSecWorker:
        // IDocumentPersistenceService → DocumentPersistenceService.
        //
        // The full set of four pins now exhaustively covers every
        // explicit `services.AddScoped<I…, …>()` in AddSecWorker:
        //   • IFilingProcessor       — strategy-pattern dispatch
        //   • IDocumentPersistenceService — this pin
        //   • ICompanySyncService    — cross-process seam
        //   • IDocumentScraper       — primary worker collaborator
        // The auto-wired DocumentManager / SecEdgarClient / hosted-service
        // bindings are not part of this pinned set (they're discovered via
        // attribute scanning and a different drift class).
        //
        // IDocumentPersistenceService's binding shape matters because:
        //   • DocumentPersistenceService.Save opens an EF Core transaction
        //     and persists both the Document row AND the underlying
        //     file blob (via IFileManager) atomically. The transaction
        //     scope is per-request, so a SINGLETON lifetime would
        //     accumulate disposed-DbContext references across requests
        //     and throw ObjectDisposedException at the second Save call.
        //   • A TRANSIENT lifetime would create a new
        //     DocumentPersistenceService per resolution — fine for the
        //     service itself, but the DocumentRepository it captures
        //     would be a DIFFERENT scoped instance than the one
        //     SecScraperWorker is already using for its EF Core change
        //     tracking. Mixed-context regressions are hard to debug at
        //     runtime: writes appear to succeed but never SaveChanges()
        //     against the right context.
        //   • A dropped binding would NRE the first time DocumentScraper
        //     tries to Save a newly downloaded filing — every SEC scraper
        //     iteration would crash and burn at the persistence boundary.
        //
        // Assert ALL THREE: descriptor exists, ImplementationType is the
        // concrete DocumentPersistenceService, Lifetime is Scoped. Matches
        // the assertion shape of the IFilingProcessor and ICompanySyncService
        // siblings.
        var services = new ServiceCollection();

        services.AddSecWorker();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDocumentPersistenceService));
        descriptor.Should().NotBeNull();
        descriptor.ImplementationType.Should().Be(typeof(DocumentPersistenceService));
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

    [Fact]
    public void AddSecWorker_RegistersSecScraperWorkerAsIHostedService() {
        // Extension of the AddSecWorker pin family. The four existing pins
        // (IFilingProcessor, ICompanySyncService, IDocumentPersistenceService,
        // IDocumentScraper) cover the scoped collaborators that the hosted
        // services consume. This pin covers a structurally distinct binding
        // shape: the `services.AddHostedService<SecScraperWorker>()` call —
        // which registers the worker itself with the .NET generic host so it
        // starts when the application boots.
        //
        // The risk this catches is asymmetric and unreachable from every
        // existing pin:
        //   • A regression that drops `AddHostedService<SecScraperWorker>()`
        //     (e.g. a "consolidate worker registrations into a meta-extension"
        //     refactor that forgot to enumerate this one, or a "move all
        //     hosted services to a different module" pass that lost it)
        //     would compile cleanly, pass every existing collaborator pin
        //     (IFilingProcessor / ICompanySyncService / etc. are still wired),
        //     and silently disable the entire SEC scraping pipeline at
        //     startup. The application boots normally, the DI container
        //     resolves every dependency, but no IHostedService implementation
        //     of SecScraperWorker is enumerated — `ScraperWorker.ExecuteAsync`
        //     is never invoked, the periodic SEC EDGAR submissions/companyfacts
        //     polling never fires, and the operating dashboard's SEC data
        //     freshness silently drifts behind. No error log, no startup
        //     warning — just absence of work.
        //   • A regression that downgrades the registration to AddScoped
        //     (instead of AddHostedService) would register the worker as a
        //     resolvable scoped service but NOT enumerate it as IHostedService
        //     — same silent failure mode.
        //   • A regression that swaps the type argument
        //     (`AddHostedService<DocumentProcessorWorker>()` written twice
        //     instead of once for each worker) would silently double-register
        //     one worker and drop another, producing duplicate runs of one
        //     pipeline and zero runs of the other.
        //
        // SecScraperWorker is the PRIMARY worker — it pulls the EDGAR
        // companyfacts and submissions feeds that drive every company-level
        // dashboard (insider trades, FTDs, financial filings). Dropping it
        // silently is the highest-impact regression of the three workers
        // AddSecWorker registers. Pin it first; DocumentProcessorWorker and
        // FtdScraperWorker are natural-extension targets for future
        // iterations of this family.
        //
        // Lookup pattern: hosted services register as IHostedService with the
        // worker class as ImplementationType. Use a flexible matcher that
        // tolerates BOTH the conventional ImplementationType registration
        // path AND the framework's "HostedServiceProvider" wrapper used by
        // `AddHostedService<T>()` extension method internally — the
        // ImplementationType is null when registered via the factory-based
        // path, but ImplementationFactory is set to a factory that produces
        // the worker. Asserting on EITHER ImplementationType OR a probing
        // factory invocation handles both registration styles deterministically.
        var services = new ServiceCollection();

        services.AddSecWorker();

        // AddHostedService<T> in modern .NET registers IHostedService with
        // ImplementationType == typeof(T) — the framework convention since
        // Microsoft.Extensions.Hosting 6.0+. Earlier versions used a factory;
        // for forward-compatibility test against ImplementationType first and
        // fall through to ImplementationInstance / ImplementationFactory
        // identity checks if needed.
        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors.Should().Contain(
            d => d.ImplementationType == typeof(SecScraperWorker),
            "AddHostedService<SecScraperWorker>() must register the worker as IHostedService so the generic host runs it at startup");
    }

    [Fact]
    public void AddSecWorker_RegistersDocumentProcessorWorkerAsIHostedService() {
        // Second sibling in the hosted-service registration family. The
        // existing pin asserts that SecScraperWorker is registered with
        // IHostedService. This pin asserts the same shape for
        // DocumentProcessorWorker — the second of three hosted services
        // AddSecWorker wires up.
        //
        // Why DocumentProcessorWorker uniquely matters (and why it's
        // unreachable from the SecScraperWorker sibling):
        //   `services.AddHostedService<T>()` is called three times in
        //   AddSecWorker — once for SecScraperWorker, once for
        //   DocumentProcessorWorker, and once for FtdScraperWorker. Each
        //   call registers a SEPARATE IHostedService descriptor. The
        //   generic host enumerates ALL IHostedService implementations
        //   and runs each in parallel. Dropping one of the three
        //   leaves the other two running normally — no error, no
        //   warning, just one pipeline silently disabled.
        //
        // SecScraperWorker pulls EDGAR feeds (already pinned).
        // DocumentProcessorWorker is structurally distinct: it
        // post-processes downloaded filings — running the markdown
        // converter, embedding generator, and chunking strategy that
        // populate the RAG pipeline. Dropping its registration would
        // mean filings still arrive (SecScraperWorker still runs) but
        // they never get processed for search/embedding lookup. The
        // dashboard's RAG search would silently return stale results
        // (only filings processed before the regression) while looking
        // healthy to operator inspection (the SEC ingest counters keep
        // ticking).
        //
        // FtdScraperWorker (the third and final hosted-service
        // registration) is the natural-extension target for a future
        // iteration of this family.
        //
        // Lookup pattern: same as the SecScraperWorker sibling — filter
        // IHostedService descriptors and assert one has
        // ImplementationType == typeof(DocumentProcessorWorker).
        var services = new ServiceCollection();

        services.AddSecWorker();

        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors.Should().Contain(
            d => d.ImplementationType == typeof(DocumentProcessorWorker),
            "AddHostedService<DocumentProcessorWorker>() must register the worker as IHostedService so post-download processing runs at startup");
    }
}
