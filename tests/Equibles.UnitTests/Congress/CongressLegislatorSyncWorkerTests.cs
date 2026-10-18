using Equibles.Congress.HostedService;
using Equibles.Congress.HostedService.Extensions;
using Equibles.Congress.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class CongressLegislatorSyncWorkerTests
{
    [Fact]
    public void SleepInterval_IsOneWeek() =>
        CreateSut().InvokeSleepInterval().Should().Be(TimeSpan.FromDays(7));

    [Fact]
    public void ErrorSource_IsCongressScraper() =>
        CreateSut().InvokeErrorSource().Should().Be(ErrorSource.CongressScraper);

    [Fact]
    public void AddCongressWorker_RegistersTheWorkerAndItsSyncService()
    {
        var services = new ServiceCollection();

        services.AddCongressWorker();

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(CongressLegislatorSyncWorker)
            );
        services.Should().Contain(d => d.ServiceType == typeof(CongressLegislatorSyncService));
        services.Should().Contain(d => d.ServiceType == typeof(CongressLegislatorClient));
    }

    private static TestableWorker CreateSut() =>
        new(
            Substitute.For<ILogger<CongressLegislatorSyncWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

    private sealed class TestableWorker : CongressLegislatorSyncWorker
    {
        public TestableWorker(
            ILogger<CongressLegislatorSyncWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter
        )
            : base(logger, scopeFactory, errorReporter) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;
    }
}
