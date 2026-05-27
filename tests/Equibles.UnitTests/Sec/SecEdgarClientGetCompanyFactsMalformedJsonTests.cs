using System.Net;
using System.Text;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to the 404 and happy-path coverage of
/// <see cref="SecEdgarClient.GetCompanyFacts"/>. The method has two distinct
/// failure-swallowing arms: <c>HttpRequestException</c> propagates, but the
/// general <c>catch (Exception)</c> at the deserialization step is documented
/// to fold non-HTTP errors into "null as not found" so a single garbled
/// payload from the SEC cannot abort a fact-ingestion run that may span
/// thousands of CIKs. Without this pin, a refactor that lets a
/// deserialization exception escape would surface only as worker crashes in
/// production. The handler below returns a 200 OK whose body is invalid JSON,
/// forcing <c>JsonConvert.DeserializeObject</c> to throw — the contract
/// promises null, not a thrown exception.
/// </summary>
public class SecEdgarClientGetCompanyFactsMalformedJsonTests
{
    [Fact]
    public async Task GetCompanyFacts_MalformedJson_ReturnsNullNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new MalformedJsonHandler()),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var act = async () => await sut.GetCompanyFacts("0000320193");

        var facts = (await act.Should().NotThrowAsync()).Subject;
        facts.Should().BeNull();
    }

    private sealed class MalformedJsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{ \"cik\": 320193, \"entityName\": \"Apple",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }
}
