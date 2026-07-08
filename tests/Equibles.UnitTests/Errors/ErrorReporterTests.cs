using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Messaging.Contracts.Activity;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;

namespace Equibles.UnitTests.Errors;

public class ErrorReporterTests
{
    [Fact]
    public async Task Report_PublishesScraperActivityWithErrorSeverity()
    {
        // Error rows on the database power the static Status page; the activity
        // feed needs the same signal in real time. Pin the IBus.Publish call so
        // a future refactor that drops the publish (or downgrades severity)
        // breaks this test instead of silently disabling the live feed.
        var bus = Substitute.For<IBus>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns(bus);
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        await sut.Report(
            ErrorSource.YahooPriceScraper,
            context: "Yahoo.Fetch",
            message: "rate limited",
            stackTrace: null
        );

        var captured = bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(c => (ScraperActivity)c.GetArguments()[0]!)
            .ToList();

        captured.Should().HaveCount(1);
        captured[0].Source.Should().Be(ErrorSource.YahooPriceScraper.Value);
        captured[0].Severity.Should().Be(ScraperActivitySeverity.Error);
        captured[0].Message.Should().Contain("Yahoo.Fetch").And.Contain("rate limited");
    }

    [Fact]
    public async Task Report_BusPublishThrows_SwallowsAndContinues()
    {
        // A broker hiccup must never propagate through ErrorReporter — it's
        // called from every scraper's catch block, and rethrowing would
        // replace the original error in the activity feed with a Publish
        // failure that's not what the operator needs to see.
        var bus = Substitute.For<IBus>();
        bus.Publish(Arg.Any<ScraperActivity>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("broker down")));
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns(bus);
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        var act = () => sut.Report(ErrorSource.Other, "Ctx", "msg", stackTrace: null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Report_NoBusRegistered_DoesNotThrow()
    {
        // The activity feed is opportunistic — a host without IBus
        // (tests, future micro-host slices) must still let ErrorReporter run.
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns((object?)null);
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        var act = () => sut.Report(ErrorSource.Other, "Ctx", "msg", stackTrace: null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Report_Exception_Cancellation_IsSkippedEntirely()
    {
        // A cancelled operation (graceful shutdown/deploy, or an inner command/HTTP
        // timeout that aborts one item) is not a fault worth an Errors row — recording
        // "The operation was canceled" only buries real errors. The typed overload drops
        // it by type before touching the scope factory or the activity feed. Pin that:
        // no scope is created (so ErrorManager is never asked to persist) and nothing is
        // published. TaskCanceledException derives from OperationCanceledException, so the
        // most common shape (a timed-out awaited task) is covered by the same check.
        var bus = Substitute.For<IBus>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns(bus);
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        await sut.Report(
            ErrorSource.YahooPriceScraper,
            context: "ReconcilePendingSplits(JILL)",
            exception: new TaskCanceledException("The operation was canceled.")
        );

        scopeFactory.DidNotReceive().CreateScope();
        bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task Report_Exception_NonCancellation_RecordsAndPublishes()
    {
        // A genuine fault still flows through: the typed overload forwards the exception's
        // message and stack trace to the string-based Report, which publishes the same
        // activity-feed signal the happy-path test pins above. This guards against the skip
        // check being widened to swallow every exception.
        var bus = Substitute.For<IBus>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns(bus);
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        await sut.Report(
            ErrorSource.FinancialFactsScraper,
            context: "FinancialFactsImport.Import",
            exception: new InvalidOperationException("XBRL envelope too large")
        );

        var captured = bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(c => (ScraperActivity)c.GetArguments()[0]!)
            .ToList();

        captured.Should().HaveCount(1);
        captured[0].Severity.Should().Be(ScraperActivitySeverity.Error);
        captured[0]
            .Message.Should()
            .Contain("FinancialFactsImport.Import")
            .And.Contain("XBRL envelope too large");
    }

    [Fact]
    public async Task Report_Exception_WrappedInnerCause_SurfacesTheInnerMessage()
    {
        // A DbUpdateException's own message is "...See the inner exception for details." — useless
        // on the Errors list. The typed overload flattens the inner chain into the recorded message
        // (which the activity feed echoes), so the real cause is visible without expanding the stack.
        var bus = Substitute.For<IBus>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBus)).Returns(bus);
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        var wrapped = new InvalidOperationException(
            "An error occurred while saving the entity changes. See the inner exception for details.",
            new Exception(
                "23505: duplicate key value violates unique constraint \"IX_IrEvent_Url\""
            )
        );

        await sut.Report(ErrorSource.Other, context: "IrEventFlow.Plan(FASLF)", exception: wrapped);

        var captured = bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IBus.Publish))
            .Select(c => (ScraperActivity)c.GetArguments()[0]!)
            .Single();
        captured.Message.Should().Contain("duplicate key value violates unique constraint");
    }

    [Fact]
    public async Task Report_ScopeFactoryThrows_DoesNotPropagate()
    {
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
        scopeFactory
            .CreateScope()
            .Returns(_ => throw new InvalidOperationException("scope unavailable"));
        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        var act = () =>
            sut.Report(ErrorSource.CftcScraper, "TestContext", "test message", stackTrace: null);

        await act.Should().NotThrowAsync();
    }
}
