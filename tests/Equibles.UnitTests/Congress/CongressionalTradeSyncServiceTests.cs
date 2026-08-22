using Equibles.Congress.HostedService.Services;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeSyncServiceTests
{
    [Fact]
    public async Task SyncAll_MinSyncDateBeforeStockAct_ClampsFromDateTo20120401()
    {
        // Congressional trade disclosures only exist from the STOCK Act's effective date
        // (2012-04-01). If an operator configures WorkerOptions.MinSyncDate to anything
        // earlier (e.g. a fresh deployment defaulting to the start of historical financial
        // data at 2000), passing that pre-STOCK-Act date to the Senate/House disclosure
        // endpoints would 400 or return junk — both APIs reject queries outside their
        // documented window. SyncAll guards against this with
        // `if (fromDate < EarliestAvailableDate) fromDate = EarliestAvailableDate;`
        // and logs the resolved window on the next line. Pin the clamp by feeding a
        // year-2000 MinSyncDate and asserting the "Starting congressional trade sync"
        // log line names 2012-04-01 as the from-date. The substituted scope factory
        // returns no SenateDisclosureClient / HouseDisclosureClient, so the Fetch
        // helpers throw, the catch blocks call ErrorReporter (whose own scope is also
        // empty — Report degrades to a Debug log), and SyncAll returns cleanly after
        // the early log fired. This isolates the clamping arithmetic from the network
        // path that's otherwise impossible to substitute (the disclosure clients are
        // sealed concretions without interfaces).
        var logger = Substitute.For<ILogger<CongressionalTradeSyncService>>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var workerOptions = Options.Create(
            new WorkerOptions { MinSyncDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
        var filingLedger = Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null);

        var sut = new CongressionalTradeSyncService(
            scopeFactory,
            workerOptions,
            logger,
            errorReporter,
            filingLedger,
            Substitute.For<CongressionalTradeImportLedger>((IServiceScopeFactory)null)
        );

        await sut.SyncAll(CancellationToken.None);

        // Inspect the structured log state directly so the assertion is culture-independent —
        // DateOnly.ToString() on the rendered message uses the host's short-date pattern
        // (e.g. "04/01/2012" on en-US, "01/04/2012" on en-GB), which would make a text
        // match flaky across machines.
        logger
            .Received()
            .Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(state => StateContainsFrom(state, new DateOnly(2012, 4, 1))),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception, string>>()
            );
    }

    [Fact]
    public async Task SyncAll_NullMinSyncDate_DefaultsToStartOfCurrentYear()
    {
        // Sibling to the STOCK-Act-clamp pin above. The risk this catches is the
        // PRODUCTION-DEFAULT branch of the same expression:
        //   var fromDate = _workerOptions.MinSyncDate.HasValue
        //       ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
        //       : new DateOnly(DateTime.UtcNow.Year, 1, 1);
        // Most deployments DON'T set MinSyncDate (it's an optional override for
        // backfills and tests), so the else-branch is the path that 99% of
        // production runs hit. The existing pin only exercises the HasValue path
        // with a pre-STOCK-Act date — it has nothing to say about the null path.
        //
        // The year-to-date window closes the gap between a rolling lookback and
        // the archive lane, which intentionally processes only completed years.
        //
        // Construction mirrors the sibling test: substituted scope factory
        // returns no Senate/House clients, the Fetch helpers throw, both
        // catches degrade cleanly, and SyncAll returns after the startup
        // "Starting congressional trade sync" log line fires with the resolved
        // From.
        var logger = Substitute.For<ILogger<CongressionalTradeSyncService>>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var workerOptions = Options.Create(new WorkerOptions { MinSyncDate = null });
        var filingLedger = Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null);

        var expectedFrom = new DateOnly(DateTime.UtcNow.Year, 1, 1);

        var sut = new CongressionalTradeSyncService(
            scopeFactory,
            workerOptions,
            logger,
            errorReporter,
            filingLedger,
            Substitute.For<CongressionalTradeImportLedger>((IServiceScopeFactory)null)
        );

        await sut.SyncAll(CancellationToken.None);

        logger
            .Received()
            .Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(state => StateContainsFrom(state, expectedFrom)),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception, string>>()
            );
    }

    private static bool StateContainsFrom(object state, DateOnly expected)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object>> values)
            return false;
        foreach (var kv in values)
        {
            if (kv.Key == "From" && kv.Value is DateOnly d && d == expected)
                return true;
        }
        return false;
    }
}
