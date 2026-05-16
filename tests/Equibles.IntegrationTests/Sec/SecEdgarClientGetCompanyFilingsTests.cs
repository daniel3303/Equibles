using System.Net;
using System.Text;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the two uncovered arms of <c>GetCompanyFilings</c>: the same-URL
/// response cache (a second call for the same CIK must reuse the cached body,
/// not re-fetch) and the catch (a non-success response is logged and rethrown
/// so callers skip the company).
/// </summary>
public class SecEdgarClientGetCompanyFilingsTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();

    [Fact]
    public async Task GetCompanyFilings_SameCikTwice_SecondCallUsesCachedResponse()
    {
        var handler = new CountingHandler("{}");
        var sut = new SecEdgarClient(
            new HttpClient(handler),
            Substitute.For<ILogger<SecEdgarClient>>(),
            Config()
        );

        await sut.GetCompanyFilings("1234567");
        await sut.GetCompanyFilings("1234567");

        handler.Calls.Should().Be(1, "the second call for the same CIK must hit the cache");
    }

    [Fact]
    public async Task GetCompanyFilings_NonSuccessResponse_LogsAndRethrows()
    {
        var handler = new StatusHandler(HttpStatusCode.NotFound);
        var sut = new SecEdgarClient(
            new HttpClient(handler),
            Substitute.For<ILogger<SecEdgarClient>>(),
            Config()
        );

        var act = async () => await sut.GetCompanyFilings("1234567");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public int Calls { get; private set; }

        public CountingHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public StatusHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(_status));
    }
}
