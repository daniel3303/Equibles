using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Errors;

public class ErrorReporterTests {
    [Fact]
    public async Task Report_ScopeFactoryThrows_DoesNotPropagate() {
        // ErrorReporter.Report is called from inside `catch` blocks across every scraper
        // (CftcImportService, FtdImportService, DocumentScraper, CongressionalTradeSyncService,
        // SecScraperWorker, ...). If Report itself threw, the original failure path would
        // bubble a *different* exception out of the catch, masking the real error and
        // potentially terminating the scraper cycle entirely. The catch-all at
        // ErrorReporter.cs:24 is the safety net that turns reporter-side failures into a
        // debug log instead of a crash.
        //
        // The risk this test pins: a refactor that narrows the `catch (Exception ex)` to
        // a specific exception type, or that moves the try/catch elsewhere, would
        // re-introduce the cascade-failure path. The only existing test for ErrorReporter
        // is an integration test that exercises the happy path with a working DI scope —
        // it would pass even with the catch removed.
        //
        // We simulate a broken DI configuration (the most realistic real-world cause:
        // ErrorManager not registered in the test fixture, or scope factory disposed)
        // by making CreateScope itself throw. CreateAsyncScope is an extension method
        // that calls CreateScope, so the throw propagates identically.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("scope unavailable"));
        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        var act = () => sut.Report(ErrorSource.CftcScraper, "TestContext", "test message", stackTrace: null);

        await act.Should().NotThrowAsync();
    }
}
