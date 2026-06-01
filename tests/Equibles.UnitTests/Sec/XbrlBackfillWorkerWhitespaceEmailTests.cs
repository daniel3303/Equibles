using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class XbrlBackfillWorkerWhitespaceEmailTests
{
    [Fact]
    public void ValidateConfiguration_SecContactEmailWhitespaceOnly_ReturnsFalse()
    {
        // Contract: a whitespace-only Sec:ContactEmail yields a User-Agent with no real
        // contact (SEC 403-bans an unidentified source), so the gate must reject it just
        // like an unset value — matching the FinancialFacts/Holdings workers.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "   " })
            .Build();
        var sut = new TestableXbrlBackfillWorker(
            Substitute.For<ILogger<XbrlBackfillWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new XbrlCaptureOptions()),
            config
        );

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    private sealed class TestableXbrlBackfillWorker : XbrlBackfillWorker
    {
        public TestableXbrlBackfillWorker(
            ILogger<XbrlBackfillWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<XbrlCaptureOptions> captureOptions,
            IConfiguration configuration
        )
            : base(
                logger,
                scopeFactory,
                errorReporter,
                captureOptions,
                Options.Create(new WorkerOptions()),
                configuration
            ) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
