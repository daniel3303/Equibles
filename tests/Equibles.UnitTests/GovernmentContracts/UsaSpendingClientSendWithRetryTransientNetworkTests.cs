using System.Net;
using System.Net.Sockets;
using Equibles.Integrations.GovernmentContracts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Pins the transport-level retry branch of <see cref="UsaSpendingClient"/>'s SendWithRetry. A
/// DNS/socket/TLS blip or request timeout reaching api.usaspending.gov surfaces as an
/// <see cref="HttpRequestException"/> that never reaches the status-code checks; before the fix it
/// bubbled straight to the import, failing the window and — repeated across every window of a backfill
/// — flooding the dashboard with hundreds of identical "An error occurred while sending the request"
/// rows. It must now be retried like a 5xx so a momentary hiccup is transparent.
/// </summary>
public class UsaSpendingClientSendWithRetryTransientNetworkTests
{
    [Fact]
    public async Task GetContractAwards_FirstCallTransportFailure_RetriesThenSucceeds()
    {
        var okBody =
            "{\"results\":[{\"generated_internal_id\":\"CONT_AWD_1\",\"Award ID\":\"PIID-1\"}],"
            + "\"page_metadata\":{\"page\":1,\"hasNext\":false}}";
        var handler = new TransientThenOkHandler(okBody);
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        var awards = await sut.GetContractAwards(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            minimumAmount: 0m
        );

        // Two calls + a parsed award prove the transport failure drove a retry that then succeeded —
        // before the fix the first throw aborted the window with no status-code branch to catch it.
        handler.CallCount.Should().Be(2);
        awards.Should().ContainSingle().Which.GeneratedInternalId.Should().Be("CONT_AWD_1");
    }

    private sealed class TransientThenOkHandler : HttpMessageHandler
    {
        private readonly string _successBody;
        public int CallCount { get; private set; }

        public TransientThenOkHandler(string successBody)
        {
            _successBody = successBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new HttpRequestException(
                    "An error occurred while sending the request.",
                    new SocketException()
                );
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_successBody),
                }
            );
        }
    }
}
