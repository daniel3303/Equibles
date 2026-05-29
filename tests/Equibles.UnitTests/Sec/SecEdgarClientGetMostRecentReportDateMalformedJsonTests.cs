using System.Net;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins GetMostRecentReportDate's graceful-degradation arm (previously
/// uncovered): a malformed submissions payload must be swallowed and return
/// null, so one company's bad JSON never crashes the document scraper — unlike
/// a transport failure, which the method deliberately rethrows.
/// </summary>
public class SecEdgarClientGetMostRecentReportDateMalformedJsonTests
{
    [Fact]
    public async Task GetMostRecentReportDate_MalformedSubmissionsJson_ReturnsNull()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new SingleResponseHandler("this is not valid json {")),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var result = await sut.GetMostRecentReportDate("0000320193", DocumentTypeFilter.TenK);

        result.Should().BeNull();
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
