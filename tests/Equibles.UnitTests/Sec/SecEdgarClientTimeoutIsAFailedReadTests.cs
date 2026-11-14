using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// A stalled SEC response ends as the HttpClient timeout. Answering null for it told callers the
/// registrant or its facts do not exist: a ticker collision then kept the wrong CIK for good, and
/// the facts import completed a cycle without one of the issuer's CIKs.
/// </summary>
public class SecEdgarClientTimeoutIsAFailedReadTests
{
    [Fact]
    public async Task GetCompanyMetadata_RequestTimesOut_ThrowsLikeAnHttpFailure()
    {
        var act = async () => await Sut().GetCompanyMetadata("0000320193");

        (
            await act.Should().ThrowAsync<HttpRequestException>()
        ).WithInnerException<OperationCanceledException>();
    }

    [Fact]
    public async Task GetCompanyFacts_RequestTimesOut_ThrowsLikeAnHttpFailure()
    {
        var act = async () => await Sut().GetCompanyFacts("0000320193");

        (
            await act.Should().ThrowAsync<HttpRequestException>()
        ).WithInnerException<OperationCanceledException>();
    }

    private static SecEdgarClient Sut() =>
        new(
            new HttpClient(new TimedOutHandler()),
            Substitute.For<ILogger<SecEdgarClient>>(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
                )
                .Build()
        );

    // What HttpClient raises when its Timeout elapses.
    private sealed class TimedOutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            throw new TaskCanceledException(
                "The request was canceled due to the configured timeout."
            );
    }
}
