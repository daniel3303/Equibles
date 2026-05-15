using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The happy path of <c>GetCompanyMetadata</c> is pinned; the
/// <c>catch (HttpRequestException)</c> branch (log + <c>throw;</c>) is not.
/// That distinction is load-bearing: HTTP failures must propagate so
/// CompanySyncService aborts and retries the cycle, whereas non-HTTP errors
/// (deserialisation) return null = "not found". A regression that moved the
/// rethrow into the generic catch would silently mark every company as
/// metadata-less during an SEC outage, mass-misclassifying issuers.
/// </summary>
public class SecEdgarClientGetCompanyMetadataHttpErrorTests
{
    [Fact]
    public async Task GetCompanyMetadata_PersistentHttpError_PropagatesHttpRequestException()
    {
        // 404 is neither 429 nor 5xx, so SendWithRetryAsync returns it without
        // retrying; EnsureSuccessStatusCode then throws into the HTTP catch.
        var handler = new ConstantStatusHandler(HttpStatusCode.NotFound);
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

        var act = () => sut.GetCompanyMetadata("1234567");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class ConstantStatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public ConstantStatusHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(_status));
    }
}
