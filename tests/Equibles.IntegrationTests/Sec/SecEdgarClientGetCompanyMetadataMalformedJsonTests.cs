using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Companion to the HTTP-error pin (which proves HttpRequestException
/// propagates). This pins the *other* catch: a 200 response whose body is not
/// valid JSON makes <c>JsonConvert.DeserializeObject</c> throw, which the
/// generic <c>catch (Exception)</c> must turn into <c>null</c> ("not found"),
/// NOT a propagated crash. The two catches encode opposite contracts; a
/// regression that merged them would either abort the whole company sync on
/// one corrupt SEC payload, or silently swallow a real outage.
/// </summary>
public class SecEdgarClientGetCompanyMetadataMalformedJsonTests
{
    [Fact]
    public async Task GetCompanyMetadata_TwoHundredWithMalformedJson_ReturnsNull()
    {
        var handler = new ConstantBodyHandler("{ this is not valid json ");
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

        var metadata = await sut.GetCompanyMetadata("1234567");

        metadata.Should().BeNull();
    }

    private sealed class ConstantBodyHandler : HttpMessageHandler
    {
        private readonly string _body;

        public ConstantBodyHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) }
            );
    }
}
