using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="SecEdgarClientFiscalYearEndCacheTests"/>,
/// which only feeds well-formed submissions JSON. GetCompanyMetadata's
/// contract is explicit: "Non-HTTP errors (deserialization, etc.) — return
/// null as 'not found'". SEC occasionally serves a 200 with a truncated/HTML
/// body; that must yield null (treated as "no metadata"), never throw — a
/// throw here would abort the best-effort fiscal-year detection it feeds.
/// </summary>
public class SecEdgarClientGetCompanyMetadataMalformedJsonTests
{
    [Fact]
    public async Task GetCompanyMetadata_Http200MalformedJsonBody_ReturnsNullNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new MalformedBodyHandler()),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var act = async () => await sut.GetCompanyMetadata("0000320193");

        var metadata = (await act.Should().NotThrowAsync()).Subject;
        metadata.Should().BeNull();
    }

    private sealed class MalformedBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{ this is not valid json"),
                }
            );
    }
}
