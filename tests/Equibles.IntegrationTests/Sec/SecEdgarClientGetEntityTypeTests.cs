using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// <c>GetEntityType</c> is the thin delegating wrapper CompanySyncService calls
/// per CIK to decide operating-company vs non-issuer. It was only ever exercised
/// through an NSubstitute mock — the real method (and the
/// <c>apiResponse == null</c> branch of <c>GetCompanyMetadata</c> it sits on)
/// had no coverage. This pins the null-safe path: when the SEC submissions
/// endpoint returns a literal JSON <c>null</c>, <c>GetEntityType</c> must yield
/// <c>null</c> via <c>metadata?.EntityType</c>, not NRE. Dropping the
/// null-conditional would crash the entire company sync on the first CIK with
/// an empty submissions document.
/// </summary>
public class SecEdgarClientGetEntityTypeTests
{
    [Fact]
    public async Task GetEntityType_SubmissionsJsonIsLiteralNull_ReturnsNullWithoutThrowing()
    {
        var handler = new ConstantHandler("null");
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

        var entityType = await sut.GetEntityType("1234567");

        entityType.Should().BeNull();
    }

    private sealed class ConstantHandler : HttpMessageHandler
    {
        private readonly string _body;

        public ConstantHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) }
            );
    }
}
