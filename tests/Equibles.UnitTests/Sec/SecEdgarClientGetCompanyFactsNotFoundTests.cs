using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial test for <see cref="SecEdgarClient.GetCompanyFacts"/>. Its
/// contract is explicit: "Returns null when the company has no XBRL facts
/// (404)". Companies without ingested XBRL are the common case, and the SEC
/// Company Facts endpoint serves a 404 for them. The 404 must be swallowed to
/// null *before* EnsureSuccessStatusCode runs — otherwise every fact-less
/// company throws HttpRequestException, which the import service rethrows and
/// aborts ingestion. This pins that the 404 branch returns null, not throws.
/// </summary>
public class SecEdgarClientGetCompanyFactsNotFoundTests
{
    [Fact]
    public async Task GetCompanyFacts_Http404_ReturnsNullNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new NotFoundHandler()),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var act = async () => await sut.GetCompanyFacts("0000320193");

        var facts = (await act.Should().NotThrowAsync()).Subject;
        facts.Should().BeNull();
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
