using System.Net;
using System.Reflection;
using System.Text;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// <c>FinraClient.SendWithRetry</c>'s transient-failure arms (429 + 5xx
/// exponential-backoff retry) were uncovered. These drive them through the
/// public <c>GetDailyShortVolume</c>: the OAuth token is pre-seeded into the
/// client's static cache (so no token round-trip), the first data response is
/// the transient status, and the retried request returns an empty 200 body —
/// the call must succeed, not surface the max-retries exception.
/// </summary>
public class FinraClientRetryTests
{
    private static readonly FieldInfo TokenField = typeof(FinraClient).GetField(
        "_cachedToken",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    private static readonly FieldInfo ExpiryField = typeof(FinraClient).GetField(
        "_tokenExpiry",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static async Task WithSeededToken(Func<Task> body)
    {
        var prevToken = TokenField.GetValue(null);
        var prevExpiry = ExpiryField.GetValue(null);
        try
        {
            TokenField.SetValue(null, "test-token");
            ExpiryField.SetValue(null, DateTime.UtcNow.AddHours(1));
            await body();
        }
        finally
        {
            TokenField.SetValue(null, prevToken);
            ExpiryField.SetValue(null, prevExpiry);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetDailyShortVolume_TransientThenOk_RetriesWithBackoffAndReturns(
        HttpStatusCode transientStatus
    )
    {
        await WithSeededToken(async () =>
        {
            var handler = new SequenceHandler(transientStatus);
            var options = Options.Create(
                new FinraOptions { ClientId = "id", ClientSecret = "secret" }
            );
            var client = new FinraClient(
                new HttpClient(handler),
                Substitute.For<ILogger<FinraClient>>(),
                options
            );

            var result = await client.GetDailyShortVolume(new DateOnly(2026, 1, 5));

            result.Should().BeEmpty("the retried request returned an empty 200 body");
            handler.CallCount.Should().Be(2, "the transient failure must be retried once");
        });
    }

    // First call: the transient status. Every later call: 200 with an empty
    // JSON array (PostQuery deserializes it to an empty record list).
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _firstStatus;
        public int CallCount { get; private set; }

        public SequenceHandler(HttpStatusCode firstStatus) => _firstStatus = firstStatus;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            if (CallCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(_firstStatus));
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
