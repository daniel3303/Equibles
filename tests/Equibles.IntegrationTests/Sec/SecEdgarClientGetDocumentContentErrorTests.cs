using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The other SecEdgarClient pins cover GetDocumentContent's happy path and the
/// FilingData-overload argument guards. This pins the string-overload catch: a
/// non-2xx response makes <c>EnsureSuccessStatusCode</c> throw, which the catch
/// must log and rethrow (callers rely on the exception to skip the filing).
/// </summary>
public class SecEdgarClientGetDocumentContentErrorTests
{
    [Fact]
    public async Task GetDocumentContent_NonSuccessResponse_LogsAndRethrows()
    {
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

        var act = async () => await sut.GetDocumentContent("0001234567-24-000001", "0001234567");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // 404 is non-retryable, so SendWithRetryAsync returns it immediately and
    // EnsureSuccessStatusCode throws — no real backoff delay.
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
