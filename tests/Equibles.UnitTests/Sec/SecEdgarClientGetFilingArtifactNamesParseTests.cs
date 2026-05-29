using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins GetFilingArtifactNames' parse path (only the empty-CIK guard was
/// covered): it returns every directory item's name, skipping items whose name
/// is missing/empty. A regression that dropped the name filter would surface
/// blank artifact names to callers iterating the filing's files.
/// </summary>
public class SecEdgarClientGetFilingArtifactNamesParseTests
{
    [Fact]
    public async Task GetFilingArtifactNames_IndexWithBlankNameItem_ReturnsOnlyNamedArtifacts()
    {
        const string indexJson =
            "{\"directory\":{\"item\":["
            + "{\"name\":\"primary_doc.xml\",\"type\":\"text.xml\"},"
            + "{\"name\":\"\",\"type\":\"folder\"},"
            + "{\"name\":\"form13fInfoTable.xml\",\"type\":\"text.xml\"}"
            + "]}}";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new SingleResponseHandler(indexJson)),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var names = await sut.GetFilingArtifactNames("0000320193", "0000320193-24-000001");

        names.Should().HaveCount(2); // the blank-name item is excluded
        names.Should().Contain("primary_doc.xml").And.Contain("form13fInfoTable.xml");
    }

    private sealed class SingleResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
    }
}
