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

public class CongressionalAnnualDisclosureSyncServiceOrchestrationTests
{
    private sealed class EmptySenateSession : ISenateBrowserSession
    {
        public Task EnsureAuthenticated(CancellationToken ct) => Task.CompletedTask;

        public Task<SenateFetchResult> Fetch(
            string url,
            Dictionary<string, string> formFields,
            CancellationToken ct
        ) => Task.FromResult(new SenateFetchResult { Status = 200, Body = "{\"data\":[]}" });

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
    public async Task SyncAll_NoReports_StillReconcilesReviewedMemberAliases()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (
                typeof(HouseAnnualReportClient),
                new HouseAnnualReportClient(
                    new HttpClient(new NotFoundHandler()),
                    Substitute.For<ILogger<HouseAnnualReportClient>>()
                )
            ),
            (
                typeof(SenateAnnualReportClient),
                new SenateAnnualReportClient(
                    new EmptySenateSession(),
                    Substitute.For<ILogger<SenateAnnualReportClient>>()
                )
            )
        );
        var filingLedger = Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null);
        filingLedger
            .GetProcessedSourceIds(
                Arg.Any<CongressionalFilingKind>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<int>(),
                Arg.Any<int>()
            )
            .Returns(new HashSet<string>());
        var identityService = Substitute.For<ICongressMemberIdentityService>();
        var sut = new CongressionalAnnualDisclosureSyncService(
            scopeFactory,
            Options.Create(new WorkerOptions { MinSyncDate = DateTime.UtcNow.Date }),
            Substitute.For<ILogger<CongressionalAnnualDisclosureSyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            filingLedger,
            identityService
        );

        await sut.SyncAll(CancellationToken.None);

        await identityService.Received(1).ReconcileMembers(CancellationToken.None);
    }
}
