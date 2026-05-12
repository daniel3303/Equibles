using Equibles.Errors.BusinessLogic;
using Equibles.Sec.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class SecScraperWorkerTests {
    [Fact]
    public void ValidateConfiguration_SecContactEmailMissing_ReturnsFalse() {
        // SEC EDGAR rejects any request whose User-Agent header lacks a contact email —
        // and unlike the FTD endpoint (covered by FtdScraperWorker test, PR #137), the
        // submissions API does it with a silent 403 + IP-ban risk that affects every other
        // SEC scraper sharing the same outbound IP. ValidateConfiguration is the startup
        // guard that keeps SecScraperWorker from looping uselessly — and from triggering
        // the ban — when the operator forgot to set Sec:ContactEmail in their .env.
        //
        // This `[Fact]` mirrors the FtdScraperWorker test pattern exactly: an empty
        // in-memory configuration, a TestableSecScraperWorker subclass that exposes the
        // protected ValidateConfiguration via a public invoker. Asserts the guard returns
        // false on a missing key. A regression that renamed the config key or removed the
        // null-check would surface here rather than as a silent 403 storm in production.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
        var sut = new TestableSecScraperWorker(
            Substitute.For<ILogger<SecScraperWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>()),
            config);

        sut.InvokeValidateConfiguration().Should().BeFalse();
    }

    private sealed class TestableSecScraperWorker : SecScraperWorker {
        public TestableSecScraperWorker(
            ILogger<SecScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IConfiguration configuration)
            : base(logger, scopeFactory, errorReporter, configuration) { }

        public bool InvokeValidateConfiguration() => ValidateConfiguration();
    }
}
