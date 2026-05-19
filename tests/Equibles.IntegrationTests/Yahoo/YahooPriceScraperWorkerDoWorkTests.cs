using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Equibles.Yahoo.HostedService;
using Equibles.Yahoo.HostedService.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// YahooPriceScraperWorker.DoWork was uncovered. Pins the cold-start guard
/// (GH-851): when the tracked-stock universe is empty, the worker must skip
/// the import and request a soon-retry rather than no-op and sleep 24h.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class YahooPriceScraperWorkerDoWorkTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public YahooPriceScraperWorkerDoWorkTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private sealed class TestableYahooPriceScraperWorker : YahooPriceScraperWorker
    {
        public TestableYahooPriceScraperWorker(
            ILogger<YahooPriceScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<YahooPriceScraperOptions> options,
            IOptions<WorkerOptions> workerOptions
        )
            : base(logger, scopeFactory, errorReporter, options, workerOptions) { }

        public Task InvokeDoWork(CancellationToken ct) => DoWork(ct);
    }

    [Fact]
    public async Task DoWork_EmptyStockUniverse_LogsAndSkipsImportInsteadOfNoOpSleep()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = _fixture.CreateDbContext();
                _contexts.Add(ctx);
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(TickerMapService)).Returns(new TickerMapService(scopeFactory));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var logger = new CapturingLogger<YahooPriceScraperWorker>();
        var worker = new TestableYahooPriceScraperWorker(
            logger,
            scopeFactory,
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new YahooPriceScraperOptions { SleepIntervalHours = 24 }),
            Options.Create(new WorkerOptions())
        );

        await worker.InvokeDoWork(CancellationToken.None);

        logger.Messages.Should().Contain(m => m.Contains("tracked stock universe is empty"));
    }
}
