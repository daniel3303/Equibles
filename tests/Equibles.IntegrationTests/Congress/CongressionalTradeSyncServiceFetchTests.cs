using System.Net;
using Equibles.Congress.Data.Models;
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
        public int FetchCount { get; private set; }

        public Task EnsureAuthenticated(CancellationToken ct) => Task.CompletedTask;

        // Missing the required DataTables envelope makes the Senate result incomplete.
        public Task<SenateFetchResult> Fetch(
            string url,
            Dictionary<string, string> formFields,
            CancellationToken ct
        )
        {
            FetchCount++;
            return Task.FromResult(new SenateFetchResult { Status = 200, Body = "{}" });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task SyncAll_BothSourcesReturnEmpty_RunsFetchPathsAndCompletes()
    {
        var senateSession = new EmptySenateSession();
        var senate = new SenateDisclosureClient(
            senateSession,
            Substitute.For<ILogger<SenateDisclosureClient>>()
        );
        var houseHandler = new NotFoundHandler();
        var house = new HouseDisclosureClient(
            new HttpClient(houseHandler),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(SenateDisclosureClient), senate),
            (typeof(HouseDisclosureClient), house)
        );
        var importLedger = Substitute.For<CongressionalTradeImportLedger>(
            (IServiceScopeFactory)null
        );
        importLedger
            .GetNextYear(
                Arg.Any<CongressionalFilingKind>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(2018);
        var memberIdentityService = Substitute.For<ICongressMemberIdentityService>();

        var sut = new CongressionalTradeSyncService(
            scopeFactory,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null),
            importLedger,
            memberIdentityService
        );

        // Both fetch helpers run their success path; with no transactions the
        // sync logs "none found" and returns — must not throw.
        var act = async () => await sut.SyncAll(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await memberIdentityService.Received(1).ReconcileMembers(CancellationToken.None);
        senateSession
            .FetchCount.Should()
            .Be(2, "Senate receives current plus the one archive partition");
        houseHandler
            .RequestCount.Should()
            .Be(1, "House receives only its current partition this cycle");
        await importLedger
            .DidNotReceive()
            .RecordCompleted(
                CongressionalFilingKind.SenatePeriodicTransactionReport,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await importLedger
            .DidNotReceive()
            .RecordCompleted(
                CongressionalFilingKind.HousePeriodicTransactionReport,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }
}
