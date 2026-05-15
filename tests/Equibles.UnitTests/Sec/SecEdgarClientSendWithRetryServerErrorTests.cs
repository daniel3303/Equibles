using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins <see cref="SecEdgarClient"/>'s SendWithRetryAsync 5xx-retry branch
/// (lines ~384-400, zero-hit by the whole suite — every existing test returns
/// 200 on the first call). A regression that moved 5xx into the generic
/// return-immediately path would surface a transient SEC outage as a hard
/// failure of the entire company sweep instead of a transparent retry.
/// </summary>
public class SecEdgarClientSendWithRetryServerErrorTests
{
    [Fact]
    public async Task GetActiveCompanies_FirstCallServerError_RetriesThenSucceeds()
    {
        var okBody =
            "{\"fields\":[\"cik\",\"name\",\"ticker\",\"exchange\"],"
            + "\"data\":[[789019,\"MICROSOFT CORP\",\"MSFT\",\"Nasdaq\"]]}";
        var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable, okBody);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(handler),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var result = await sut.GetActiveCompanies();

        // Two calls + a parsed company prove the 503 drove a retry that then
        // succeeded — a broken 5xx branch would throw on the first response.
        handler.CallCount.Should().Be(2);
        result.Should().ContainSingle();
        result[0].Tickers.Should().ContainSingle().Which.Should().Be("MSFT");
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _firstStatus;
        private readonly string _successBody;
        public int CallCount { get; private set; }

        public SequenceHandler(HttpStatusCode firstStatus, string successBody)
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
            {
                return Task.FromResult(new HttpResponseMessage(_firstStatus));
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
