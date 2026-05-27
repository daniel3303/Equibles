using Equibles.Errors.BusinessLogic;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerWorkerNameTests
{
    // Pins the literal `WorkerName` property of FinancialFactsScraperWorker
    // — "Financial facts scraper". Sibling to the per-worker WorkerName
    // pins already in place for CboeScraperWorker, FtdScraperWorker
    // (#226), HoldingsScraperWorker, and SecScraperWorker.
    //
    // The risk this pin uniquely catches: a refactor that "harmonized"
    // worker names by prefixing all SEC subsystem workers with "SEC "
    // (e.g. "SEC Financial facts scraper") OR that abbreviated the
    // name to a single word ("FinancialFacts" / "Facts") would compile
    // cleanly, pass every existing pin, and silently break operator
    // runbook queries that filter the Serilog file by exact WorkerName
    // prefix when triaging "which SEC subsystem hit this 429 burst?".
    //
    // FinancialFactsScraperWorker is one of THREE workers in the
    // Sec.FinancialFacts namespace area whose logs land in the same
    // Serilog file as SecScraperWorker, DocumentProcessorWorker, and
    // FtdScraperWorker. The "Financial facts" prefix (two-word
    // lowercase plural) is the documented operator runbook handle —
    // distinct from "Financial Facts" (capitalised), "FinancialFacts"
    // (PascalCase), and "Facts" (abbreviated). Without an exact-string
    // pin, any of those drift variations would pass every existing
    // test in this file.
    [Fact]
    public void WorkerName_IsFinancialFactsScraper()
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

        sut.InvokeWorkerName().Should().Be("Financial facts scraper");
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

        public string InvokeWorkerName() => WorkerName;
    }
}
