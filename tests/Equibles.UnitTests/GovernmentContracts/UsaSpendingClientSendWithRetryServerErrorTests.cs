using System.Net;
using Equibles.Integrations.GovernmentContracts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Pins <see cref="UsaSpendingClient"/>'s transient-retry contract: a one-off 5xx from
/// USAspending must be retried and recovered, not surfaced as a hard failure. A regression
/// that dropped the 5xx branch would turn an upstream blip into a starved federal-contracts
/// ingest with no operator-visible exception path.
/// </summary>
public class UsaSpendingClientSendWithRetryServerErrorTests
{
    [Fact]
    public async Task GetContractAwards_FirstCallServerError_RetriesThenSucceeds()
    {
        var okBody =
            "{\"results\":[{\"generated_internal_id\":\"CONT_AWD_1\",\"Award ID\":\"PIID-1\"}],"
            + "\"page_metadata\":{\"page\":1,\"hasNext\":false}}";
        var handler = new ServerErrorThenOkHandler(HttpStatusCode.ServiceUnavailable, okBody);
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        var awards = await sut.GetContractAwards(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            minimumAmount: 0m
        );

        // Two calls + a parsed award prove the 503 drove a retry that then succeeded —
        // a broken 5xx branch would throw on the first response.
        handler.CallCount.Should().Be(2);
        awards.Should().ContainSingle().Which.GeneratedInternalId.Should().Be("CONT_AWD_1");
    }

    private sealed class ServerErrorThenOkHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _firstStatus;
        private readonly string _successBody;
        public int CallCount { get; private set; }

        public ServerErrorThenOkHandler(HttpStatusCode firstStatus, string successBody)
        {
            _firstStatus = firstStatus;
            _successBody = successBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            if (CallCount == 1)
                return Task.FromResult(new HttpResponseMessage(_firstStatus));
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_successBody),
                }
            );
        }
    }
}
