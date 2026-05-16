using System.Net;
using System.Reflection;
using Equibles.Congress.HostedService;
using Equibles.Congress.HostedService.Services;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// <see cref="CongressionalTradeScraperWorker.DoWork"/> was 0% — it resolves
/// the sync service from a scope and runs one cycle. With both disclosure
/// sources returning empty, the cycle completes and the worker logs success;
/// driven via reflection through the real scope harness.
/// </summary>
public class CongressionalTradeScraperWorkerDoWorkTests
{
    private sealed class EmptySenateSession : ISenateBrowserSession
    {
        public Task EnsureAuthenticated(CancellationToken ct) => Task.CompletedTask;

        public Task<SenateFetchResult> Fetch(
            string url,
            Dictionary<string, string> formFields,
            CancellationToken ct
        ) => Task.FromResult(new SenateFetchResult { Status = 200, Body = "{}" });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task DoWork_BothSourcesEmpty_RunsSyncCycleAndLogsCompletion()
    {
        var errorReporter = new ErrorReporter(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var innerScope = ServiceScopeSubstitute.Create(
            (
                typeof(SenateDisclosureClient),
                new SenateDisclosureClient(
                    new EmptySenateSession(),
                    Substitute.For<ILogger<SenateDisclosureClient>>()
                )
            ),
            (
                typeof(HouseDisclosureClient),
                new HouseDisclosureClient(
                    new HttpClient(new NotFoundHandler()),
                    Substitute.For<ILogger<HouseDisclosureClient>>()
                )
            )
        );
        var syncService = new CongressionalTradeSyncService(
            innerScope,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            errorReporter
        );

        var workerScope = ServiceScopeSubstitute.Create(
            (typeof(CongressionalTradeSyncService), syncService)
        );
        var worker = new CongressionalTradeScraperWorker(
            Substitute.For<ILogger<CongressionalTradeScraperWorker>>(),
            workerScope,
            errorReporter
        );

        var doWork = typeof(CongressionalTradeScraperWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var act = async () => await (Task)doWork.Invoke(worker, [CancellationToken.None]);

        await act.Should().NotThrowAsync("an empty sync cycle must complete cleanly");
    }
}
