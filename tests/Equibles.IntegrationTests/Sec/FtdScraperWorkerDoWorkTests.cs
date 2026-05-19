using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// FtdScraperWorker.DoWork was uncovered (52%). Pins the GH-851 cold-start
/// guard: when CompanySync hasn't populated CommonStock yet, the FTD import
/// would match nothing and starve the Holdings pipeline for 24h. The worker
/// must short-circuit instead.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FtdScraperWorkerDoWorkTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public FtdScraperWorkerDoWorkTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private sealed class TestableFtdScraperWorker : FtdScraperWorker
    {
        public TestableFtdScraperWorker(
            ILogger<FtdScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FtdScraperOptions> options,
            IOptions<WorkerOptions> workerOptions,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, options, workerOptions, configuration) { }

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

        var logger = new CapturingLogger<FtdScraperWorker>();
        var worker = new TestableFtdScraperWorker(
            logger,
            scopeFactory,
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FtdScraperOptions { SleepIntervalHours = 24 }),
            Options.Create(new WorkerOptions()),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>())
                .Build()
        );

        await worker.InvokeDoWork(CancellationToken.None);

        logger.Messages.Should().Contain(m => m.Contains("tracked stock universe is empty"));
    }
}
