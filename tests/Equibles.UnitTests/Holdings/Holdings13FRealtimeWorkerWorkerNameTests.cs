using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class Holdings13FRealtimeWorkerWorkerNameTests
{
    // Tenth WorkerName pin in the natural-extension family (CBOE, Holdings,
    // SEC filing, FTD, Document processor, FRED, Yahoo price, FINRA,
    // Congressional trade, and now 13F real-time ingestion). WorkerName
    // flows into BaseScraperWorker's structured log scope and shows up in
    // every Serilog line the worker emits — on-call greps
    // `data/worker/logs/log<date>.txt` for "13F real-time ingestion" when
    // institutional-holdings flow stalls before quarter-end reconciliation.
    //
    // The literal is deliberately distinct from sibling
    // `HoldingsScraperWorker` (the quarterly bulk-import worker, name
    // "Holdings scraper") even though both share `ErrorSource.HoldingsScraper`
    // for issue-tracker routing. The asymmetry is intentional:
    //   • ErrorSource is system-wide and short (one queue per data
    //     domain) — so both 13F paths converge on `HoldingsScraper`.
    //   • WorkerName is operator-facing and descriptive (one stream per
    //     worker) — so the two paths must NOT share the log name.
    //
    // The risk: a "consistency" refactor that aligns WorkerName with the
    // shared ErrorSource (e.g. naming this worker "Holdings scraper" or
    // "13F holdings scraper") would compile, pass every existing pin,
    // and silently merge two operationally distinct log streams. On-call
    // tailing for "13F real-time ingestion" during a quarter-end
    // disclosure burst would see no output even while the worker is
    // running; the bulk-import dashboard would simultaneously fill with
    // unrelated real-time noise.
    //
    // Pin the exact literal so the rename is loud, not silent.
    [Fact]
    public void WorkerName_Is13FRealtimeIngestion()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableHoldings13FRealtimeWorker(
            Substitute.For<ILogger<Holdings13FRealtimeWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            config
        );

        sut.InvokeWorkerName().Should().Be("13F real-time ingestion");
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

        public string InvokeWorkerName() => WorkerName;
    }
}
