using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerErrorSourceTests
{
    // Pins the `ErrorSource` property of FinancialFactsScraperWorker —
    // ErrorSource.FinancialFactsScraper. Sibling to the per-worker
    // ErrorSource pins in place for FtdScraperWorker (#226) and others.
    //
    // BaseScraperWorker passes the `ErrorSource` property to
    // `ErrorReporter.Report` every time it catches an exception in the
    // scrape loop. The reporter uses that enum value as the routing
    // key for the dashboard error queue: errors tagged
    // `FinancialFactsScraper` land in the financial-facts triage view,
    // while errors tagged `SecScraper` / `FtdScraper` / etc. land in
    // their respective queues.
    //
    // The risk this pin uniquely catches: a copy-paste regression from
    // the adjacent SecScraperWorker or FtdScraperWorker — e.g.
    // `protected override ErrorSource ErrorSource => ErrorSource.SecScraper;`
    // — would compile cleanly, pass every existing FinancialFactsScraperWorker
    // pin (WorkerName, ValidateConfiguration), and silently misroute every
    // Financial Facts ingestion failure into the SEC filing queue.
    // Operators triaging Financial Facts-specific outages would see no
    // alerts while the SEC filing queue accumulates confusing Company
    // Facts stack traces. The misclassification is invisible to existing
    // tests because they only exercise WorkerName and ValidateConfiguration.
    //
    // Pin the literal enum value so the next reordering or copy-paste
    // must update this test deliberately.
    [Fact]
    public void ErrorSource_IsFinancialFactsScraper()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableFinancialFactsScraperWorker(
            Substitute.For<ILogger<FinancialFactsScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FinancialFactsScraperOptions()),
            config
        );

        sut.InvokeErrorSource().Should().Be(ErrorSource.FinancialFactsScraper);
    }

    private sealed class TestableFinancialFactsScraperWorker : FinancialFactsScraperWorker
    {
        public TestableFinancialFactsScraperWorker(
            ILogger<FinancialFactsScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FinancialFactsScraperOptions> options,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, options, configuration) { }

        public ErrorSource InvokeErrorSource() => ErrorSource;
    }
}
