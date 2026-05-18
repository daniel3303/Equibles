using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class Holdings13FRealtimeWorkerTests
{
    [Fact]
    public void ValidateConfiguration_SecContactEmailWhitespaceOnly_ReturnsFalse()
    {
        // Contract (from the worker's own warning + the SEC EDGAR User-Agent rule it
        // guards): the worker must stop when the contact email is "not configured".
        // A whitespace-only value (e.g. `SEC_CONTACT_EMAIL= ` in .env) is not a usable
        // contact — SEC silently 403s a blank contact — so it must be treated as
        // unconfigured and return false, exactly like an empty value.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "   " })
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

        sut.InvokeValidateConfiguration().Should().BeFalse();
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

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
