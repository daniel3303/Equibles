using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the operator-facing identity of the dimensional-fact extraction sweep:
/// the exact WorkerName operators grep Serilog files by (sibling to the
/// per-worker WorkerName pins for the other SEC subsystem workers) and the
/// ErrorSource its failures are filed under in the Errors table. Drift in
/// either would compile cleanly and silently break runbook queries.
/// </summary>
public class XbrlFactsExtractionWorkerIdentityTests
{
    [Fact]
    public void WorkerNameAndErrorSource_AreThePinnedOperatorHandles()
    {
        var sut = new TestableXbrlFactsExtractionWorker(
            Substitute.For<ILogger<XbrlFactsExtractionWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new XbrlFactsExtractionOptions())
        );

        sut.InvokeWorkerName().Should().Be("XBRL facts extraction");
        sut.InvokeErrorSource().Should().Be(ErrorSource.FinancialFactsScraper);
    }

    private sealed class TestableXbrlFactsExtractionWorker : XbrlFactsExtractionWorker
    {
        public TestableXbrlFactsExtractionWorker(
            ILogger<XbrlFactsExtractionWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<XbrlFactsExtractionOptions> options
        )
            : base(logger, scopeFactory, errorReporter, options) { }

        public string InvokeWorkerName() => WorkerName;

        public ErrorSource InvokeErrorSource() => ErrorSource;
    }
}
