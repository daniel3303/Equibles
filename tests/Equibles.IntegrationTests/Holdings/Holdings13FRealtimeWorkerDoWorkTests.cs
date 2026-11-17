using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Holdings13FRealtimeWorker.DoWork was uncovered (25%) — only
/// ValidateConfiguration had a unit pin. Drives DoWork with an
/// `ISecEdgarClient` whose daily-index always returns no entries, so the
/// ingestion service short-circuits with zero filings and the worker logs
/// the cycle-complete line. Covers the worker's scope resolution, the call
/// into the ingestion service, and the final log emit.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Holdings13FRealtimeWorkerDoWorkTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public Holdings13FRealtimeWorkerDoWorkTests(ParadeDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    private sealed class TestableHoldings13FRealtimeWorker : Holdings13FRealtimeWorker
    {
        public TestableHoldings13FRealtimeWorker(
            ILogger<Holdings13FRealtimeWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<WorkerOptions> workerOptions,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, workerOptions, configuration) { }

        public Task InvokeDoWork(CancellationToken ct) => DoWork(ct);

        public Task InvokeWait(CancellationToken ct) => WaitForNextCycle(SleepInterval, ct);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 11)]
    public async Task DoWork_EmptyDailyIndex_PreservesConcurrentReset(
        bool concurrentReset,
        int pendingFailures
    )
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var seed = _fixture.CreateDbContext())
        {
            if (concurrentReset || pendingFailures > 0)
            {
                seed.Set<RealtimeSweepState>()
                    .Add(
                        new RealtimeSweepState
                        {
                            WorkerName = Holdings13FRealtimeWorker.WorkerStateName,
                            SweptThrough = pendingFailures > 0 ? new DateOnly(2020, 1, 1) : today,
                        }
                    );
                await seed.SaveChangesAsync();
            }
        }
        await using (var seed = _fixture.CreateDbContext())
        {
            var failures = new HoldingsImportFailureRepository(seed);
            for (var i = 0; i < pendingFailures; i++)
                await failures.Record(
                    $"pending-{i}",
                    $"{1000 + i}",
                    today,
                    today,
                    HoldingsImportFailureReason.MissingSourcePosition,
                    CancellationToken.None
                );
        }
        var resetApplied = false;
        var edgarClient = Substitute.For<ISecEdgarClient>();
        edgarClient
            .GetCompanyFilings(Arg.Any<string>(), null, Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>())
            .Returns([]);
        // EDGAR still publishes every omitted filing here, so the refusal, not the withdrawn
        // rule, is what defers each filer.
        edgarClient
            .FilingExists(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        edgarClient
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (concurrentReset && !resetApplied)
                {
                    resetApplied = true;
                    await using var reset = _fixture.CreateDbContext();
                    await new RealtimeSweepStateRepository(reset).Rewind(
                        Holdings13FRealtimeWorker.WorkerStateName,
                        today.AddDays(-90),
                        CancellationToken.None
                    );
                }
                return new List<EdgarDailyIndexEntry>();
            });

        var logger = new CapturingLogger<Holdings13FRealtimeWorker>();
        var worker = Worker(edgarClient, logger);

        await worker.InvokeDoWork(CancellationToken.None);

        if (pendingFailures > 0)
        {
            // Discovery reads the newest days; the sweep's own window stays deferred.
            FetchedDates(edgarClient).Should().OnlyContain(d => d > today.AddDays(-15));
            await using var first = _fixture.CreateDbContext();
            (
                await first
                    .Set<HoldingsImportFailure>()
                    .CountAsync(r => r.NextAttemptAt > DateTime.UtcNow)
            )
                .Should()
                .Be(10);
            (await SweepState(first)).SweptThrough.Should().Be(new DateOnly(2020, 1, 1));
            await worker.InvokeWait(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
            await worker.InvokeDoWork(CancellationToken.None);
            await worker.InvokeWait(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
        }

        await using var verify = _fixture.CreateDbContext();
        var state = await SweepState(verify);
        if (concurrentReset)
        {
            state.SweptThrough.Should().Be(today.AddDays(-90));
            (await verify.Set<ProcessedFiling>().CountAsync()).Should().Be(0);
            logger.Messages.Should().Contain(m => m.Contains("replay remains pending"));
        }
        else
        {
            var start =
                pendingFailures > 0
                    ? new DateOnly(2020, 1, 1)
                    : today.AddDays(
                        -(Holdings13FRealtimeWorker.EffectiveMinLookback(today, 7) - 1)
                    );
            state
                .SweptThrough.Should()
                .Be(Holdings13FRealtimeWorker.ComputeWindowEnd(today, start));
            var fetchedDates = FetchedDates(edgarClient)
                .Where(d => d <= state.SweptThrough)
                .ToList();
            fetchedDates.Count.Should().BeLessThanOrEqualTo(15);
            fetchedDates.Min().Should().Be(start);
            logger
                .Messages.Should()
                .Contain(m => m.Contains("13F real-time ingestion cycle complete"));
        }
    }

    /// <summary>
    /// A CUSIP rescan queues thousands of stored filings for an ordered replay and rewinds the
    /// sweep. While that drains, a filing filed today must still be queued ahead of the replays,
    /// and the replays must not hold the sweep back.
    /// </summary>
    [Fact]
    public async Task DoWork_RewoundSweepWithDueReplays_QueuesNewFilingsAndKeepsSweeping()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rewound = today.AddDays(-60);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<RealtimeSweepState>()
                .Add(
                    new RealtimeSweepState
                    {
                        WorkerName = Holdings13FRealtimeWorker.WorkerStateName,
                        SweptThrough = rewound,
                    }
                );
            // Imported before the rewind: its rows are stored, only its ledger row is gone.
            var issuer = new Equibles.CommonStocks.Data.Models.EquityIssuer { Name = "Issuer" };
            var holder = new InstitutionalHolder { Cik = "8888", Name = "Manager" };
            seed.AddRange(issuer, holder);
            seed.Add(
                new InstitutionalHolding
                {
                    EquityIssuerId = issuer.Id,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = new DateOnly(2026, 6, 30),
                    FilingDate = today,
                    FilingType = FilingType.Form13F,
                    AccessionNumber = "stored-before-rewind",
                    Shares = 17,
                }
            );
            await seed.SaveChangesAsync();
            var failures = new HoldingsImportFailureRepository(seed);
            for (var i = 0; i < 25; i++)
                await failures.EnqueueRecovery(
                    $"stored-{i}",
                    $"{1000 + i}",
                    rewound.AddDays(-30),
                    CancellationToken.None,
                    supersedesActiveAttempt: true,
                    reason: HoldingsImportFailureReason.StoredReplay
                );
        }
        var edgarClient = Substitute.For<ISecEdgarClient>();
        edgarClient
            .GetCompanyFilings(Arg.Any<string>(), null, Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>())
            .Returns([]);
        edgarClient
            .FilingExists(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        edgarClient
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<DateOnly>() == today
                    ?
                    [
                        new EdgarDailyIndexEntry
                        {
                            Cik = "0000009999",
                            AccessionNumber = "filed-today",
                            DateFiled = today,
                            FormType = "13F-HR",
                        },
                        new EdgarDailyIndexEntry
                        {
                            Cik = "0000008888",
                            AccessionNumber = "stored-before-rewind",
                            DateFiled = today,
                            FormType = "13F-HR",
                        },
                    ]
                    : new List<EdgarDailyIndexEntry>()
            );
        var worker = Worker(edgarClient, new CapturingLogger<Holdings13FRealtimeWorker>());

        await worker.InvokeDoWork(CancellationToken.None);

        await using (var first = _fixture.CreateDbContext())
        {
            var queued = await first
                .Set<HoldingsImportFailure>()
                .SingleAsync(r => r.AccessionNumber == "filed-today");
            queued.Reason.Should().Be(HoldingsImportFailureReason.PendingRecovery);
            queued.Cik.Should().Be("9999");
            // The stored filing stays with the rewound sweep, which re-applies it in order.
            (
                await first
                    .Set<HoldingsImportFailure>()
                    .CountAsync(r => r.Reason != HoldingsImportFailureReason.StoredReplay)
            )
                .Should()
                .Be(1);
            (
                await first
                    .Set<RealtimeSweepState>()
                    .SingleAsync(s => s.WorkerName == HoldingsImportCoverage.DiscoveryWorkerName)
            )
                .SweptThrough.Should()
                .Be(today);
            // The filing queued this cycle is due, so the sweep waits for its recovery.
            (await SweepState(first))
                .SweptThrough.Should()
                .Be(rewound);
        }

        await worker.InvokeDoWork(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        (
            await verify
                .Set<HoldingsImportFailure>()
                .SingleAsync(r => r.AccessionNumber == "filed-today")
        )
            .NextAttemptAt.Should()
            .BeAfter(now, "a filing not stored yet is recovered before the stored replays");
        (
            await verify
                .Set<HoldingsImportFailure>()
                .CountAsync(r =>
                    r.Reason == HoldingsImportFailureReason.StoredReplay && r.NextAttemptAt <= now
                )
        )
            .Should()
            .BeGreaterThan(0);
        (await SweepState(verify))
            .SweptThrough.Should()
            .Be(
                Holdings13FRealtimeWorker.ComputeWindowEnd(today, rewound),
                "stored replays still due must not defer the sweep"
            );
    }

    private TestableHoldings13FRealtimeWorker Worker(
        ISecEdgarClient edgarClient,
        CapturingLogger<Holdings13FRealtimeWorker> logger
    )
    {
        IServiceScopeFactory scopeFactory = null!;
        scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = _fixture.CreateDbContext();
                _contexts.Add(ctx);
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(ProcessedDataSetRepository))
                    .Returns(new ProcessedDataSetRepository(ctx));
                sp.GetService(typeof(ProcessedFilingRepository))
                    .Returns(new ProcessedFilingRepository(ctx));
                sp.GetService(typeof(HoldingsImportFailureRepository))
                    .Returns(new HoldingsImportFailureRepository(ctx));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                sp.GetService(typeof(RealtimeSweepStateRepository))
                    .Returns(new RealtimeSweepStateRepository(ctx));
                sp.GetService(typeof(HoldingsRealtimeReplaySignal))
                    .Returns(new HoldingsRealtimeReplaySignal());
                var importService = new HoldingsImportService(
                    scopeFactory,
                    Substitute.For<ILogger<HoldingsImportService>>(),
                    Options.Create(new WorkerOptions()),
                    Substitute.For<IStockPriceProvider>(),
                    Substitute.For<MassTransit.IBus>()
                );
                var ingestion = new Realtime13FIngestionService(
                    edgarClient,
                    new Filing13FXmlParser(),
                    new Realtime13FArchiveBuilder(),
                    importService,
                    scopeFactory,
                    Substitute.For<ILogger<Realtime13FIngestionService>>()
                );
                sp.GetService(typeof(Realtime13FIngestionService)).Returns(ingestion);
                sp.GetService(typeof(HoldingsImportRecoveryService))
                    .Returns(
                        new HoldingsImportRecoveryService(
                            new HoldingsImportFailureRepository(ctx),
                            new InstitutionalHoldingRepository(ctx),
                            new RealtimeSweepStateRepository(ctx),
                            edgarClient,
                            ingestion,
                            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
                        )
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        return new TestableHoldings13FRealtimeWorker(
            logger,
            scopeFactory,
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions { MinSyncDate = new DateTime(2024, 1, 1) }),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>())
                .Build()
        );
    }

    private static Task<RealtimeSweepState> SweepState(EquiblesFinancialDbContext db) =>
        db.Set<RealtimeSweepState>()
            .SingleAsync(s => s.WorkerName == Holdings13FRealtimeWorker.WorkerStateName);

    private static List<DateOnly> FetchedDates(ISecEdgarClient edgarClient) =>
        edgarClient
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ISecEdgarClient.GetDailyIndex))
            .Select(c => (DateOnly)c.GetArguments()[0])
            .ToList();
}
