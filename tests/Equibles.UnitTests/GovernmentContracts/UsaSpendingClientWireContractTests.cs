using System.Net;
using Equibles.Integrations.GovernmentContracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Pins the two wire-level properties every USAspending request must carry.
///
/// Connection: close — some backends behind USAspending's IPv4 load balancer accept a
/// connection and then close it on the first request ("empty reply" → .NET's "the response
/// ended prematurely"). A pooled connection pins the client to whichever backend it drew, so
/// once the pool holds a sick one the whole retry ladder dies on it — measured in prod while
/// fresh-connection probes of the identical query passed ~50%. Closing per request makes each
/// retry an independent backend draw, which is the property the retry ladder's math relies on;
/// silently dropping the header re-freezes the backfill during the API's bad spells.
///
/// User-Agent — .NET sends none by default, and anonymous clients are the first thing a
/// federal API's protection tightens on (SEC EDGAR outright bans them).
/// </summary>
public class UsaSpendingClientWireContractTests
{
    [Fact]
    public async Task GetContractAwards_EveryRequest_ClosesTheConnectionAndIdentifiesItself()
    {
        var handler = new CapturingHandler(
            "{\"results\":[],\"page_metadata\":{\"page\":1,\"hasNext\":false}}"
        );
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        await sut.GetContractAwards(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            minimumAmount: 0m
        );

        handler
            .ConnectionClose.Should()
            .BeTrue(
                "every request must go out on a fresh connection so retries re-roll the backend"
            );
        handler
            .UserAgent.Should()
            .NotBeNullOrWhiteSpace("the client must identify itself to the federal API");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;

        public bool? ConnectionClose { get; private set; }
        public string UserAgent { get; private set; }

        public CapturingHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            // Snapshot header values here — the client disposes the request after sending.
            ConnectionClose = request.Headers.ConnectionClose;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) }
            );
        }
    }
}
