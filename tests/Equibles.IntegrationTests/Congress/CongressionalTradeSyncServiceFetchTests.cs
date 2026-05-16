using System.Net;
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
/// <see cref="CongressionalTradeSyncServiceBothFailTests"/> pins the Fetch
/// catch arms (both sources throw). This pins their success paths: both
/// clients resolve and return (here, empty) results, so SyncAll runs
/// FetchSenate/FetchHouse to completion and takes the no-transactions branch.
/// </summary>
public class CongressionalTradeSyncServiceFetchTests
{
    private sealed class EmptySenateSession : ISenateBrowserSession
    {
        public Task EnsureAuthenticated(CancellationToken ct) => Task.CompletedTask;

        // Empty search JSON → zero reports → GetRecentTransactions returns [].
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
    public async Task SyncAll_BothSourcesReturnEmpty_RunsFetchPathsAndCompletes()
    {
        var senate = new SenateDisclosureClient(
            new EmptySenateSession(),
            Substitute.For<ILogger<SenateDisclosureClient>>()
        );
        var house = new HouseDisclosureClient(
            new HttpClient(new NotFoundHandler()),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(SenateDisclosureClient), senate),
            (typeof(HouseDisclosureClient), house)
        );

        var sut = new CongressionalTradeSyncService(
            scopeFactory,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        // Both fetch helpers run their success path; with no transactions the
        // sync logs "none found" and returns — must not throw.
        var act = async () => await sut.SyncAll(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
