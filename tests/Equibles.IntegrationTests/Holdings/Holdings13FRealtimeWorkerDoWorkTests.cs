using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
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
    private readonly List<EquiblesDbContext> _contexts = [];

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
    }

    [Fact]
    public async Task DoWork_EmptyDailyIndexAcrossLookbackWindow_CompletesWithZeroFilings()
    {
        var edgarClient = Substitute.For<ISecEdgarClient>();
        edgarClient
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<EdgarDailyIndexEntry>());

        IServiceScopeFactory scopeFactory = null!;
        scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = _fixture.CreateDbContext();
                _contexts.Add(ctx);
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesDbContext)).Returns(ctx);
                var importService = new HoldingsImportService(
                    scopeFactory,
                    Substitute.For<ILogger<HoldingsImportService>>(),
                    Options.Create(new WorkerOptions()),
                    Substitute.For<IStockPriceProvider>()
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
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var logger = new CapturingLogger<Holdings13FRealtimeWorker>();
        var worker = new TestableHoldings13FRealtimeWorker(
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

        await worker.InvokeDoWork(CancellationToken.None);

        logger.Messages.Should().Contain(m => m.Contains("13F real-time ingestion cycle complete"));
    }
}
