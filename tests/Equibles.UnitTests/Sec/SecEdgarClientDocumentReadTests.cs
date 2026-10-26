using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// <see cref="SecEdgarClient"/> used to buffer a whole submission into a string
/// with no bound. Measured 2026-10-21: one 21,369,955-character document put a
/// 1.25 GiB worker into a storm of OutOfMemoryExceptions and it stopped making
/// progress for hours, so the read now refuses anything over its limit and says
/// which URL was refused.
/// </summary>
public class SecEdgarClientDocumentReadTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(_respond());
    }

    private static SecEdgarClient CreateClient(Func<HttpResponseMessage> respond)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["Sec:ContactEmail"] = "test@example.com",
                    // Zero pause keeps the rate limiter out of the test's timing.
                    ["Sec:RateLimitPauseSeconds"] = "0",
                }
            )
            .Build();

        return new SecEdgarClient(
            new HttpClient(new StubHandler(respond)),
            Substitute.For<ILogger<SecEdgarClient>>(),
            configuration
        );
    }

    [Fact]
    public async Task GetDocumentContent_SmallBody_ReturnsIt()
    {
        var client = CreateClient(() =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>a filing</html>"),
            }
        );

        var content = await client.GetDocumentContent("0000000000-00-000000", "1");

        Assert.Equal("<html>a filing</html>", content);
    }

    [Fact]
    public async Task GetDocumentContent_BodyOverTheLimit_FailsWithoutReadingIt()
    {
        var client = CreateClient(() =>
        {
            var content = new ByteArrayContent([]);
            // Declared only: the point is that the refusal happens before the body
            // is read, so no 64 MiB allocation belongs in the test.
            content.Headers.ContentLength = 64L * 1024 * 1024 + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetDocumentContent("0000000000-00-000000", "1")
        );

        Assert.Contains("over the", exception.Message);
        Assert.Contains("limit", exception.Message);
    }
}
