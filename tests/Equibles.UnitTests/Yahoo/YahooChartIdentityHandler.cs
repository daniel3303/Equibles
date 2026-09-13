using System.Net;

namespace Equibles.UnitTests.Yahoo;

internal sealed class YahooChartIdentityHandler(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
        );
}
