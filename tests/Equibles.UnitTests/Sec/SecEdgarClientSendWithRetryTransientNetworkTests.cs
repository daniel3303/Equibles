using System.Net;
using System.Net.Sockets;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the transient-network retry branch of <see cref="SecEdgarClient"/>'s SendWithRetryAsync.
/// DNS / socket / TLS failures reaching www.sec.gov surface as an
/// <see cref="HttpRequestException"/> wrapping a <see cref="SocketException"/>; before the fix
/// these bubbled to DocumentScraper and were recorded as dashboard errors. They must now be
/// retried like a 5xx, so a blip is transparent rather than a hard failure of the sweep.
/// </summary>
public class SecEdgarClientSendWithRetryTransientNetworkTests
{
    [Fact]
    public async Task GetActiveCompanies_FirstCallDnsFailure_RetriesThenSucceeds()
    {
        var okBody =
            "{\"fields\":[\"cik\",\"name\",\"ticker\",\"exchange\"],"
            + "\"data\":[[789019,\"MICROSOFT CORP\",\"MSFT\",\"Nasdaq\"]]}";
        var handler = new TransientThenOkHandler(okBody);
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

        // Two calls + a parsed company prove the DNS/socket failure drove a retry that then
        // succeeded — without the fix the first throw would abort the whole sweep.
        handler.CallCount.Should().Be(2);
        result.Should().ContainSingle();
        result[0].Tickers.Should().ContainSingle().Which.Should().Be("MSFT");
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
                    "Name or service not known (www.sec.gov:443)",
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
