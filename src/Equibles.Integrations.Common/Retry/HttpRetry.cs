using System.Net;
using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.Integrations.Common.Retry;

public static class HttpRetry
{
    // Shared 429/5xx retry loop for the simple integration clients (FRED, CFTC,
    // CBOE): wait on the rate limiter, send, and on a throttle (429) or server
    // error (5xx) back off via RetryBackoff and retry up to maxRetries. The first
    // successful response is returned undisposed for the caller to read; responses
    // from retried attempts are disposed here. Only a 429 pauses the shared limiter
    // (a 5xx is not a rate-limit signal). Clients that layer auth refresh,
    // Retry-After, or transient-network handling on top (FINRA, Yahoo, SEC) keep
    // their own loops.
    public static async Task<HttpResponseMessage> Send(
        Func<Task<HttpResponseMessage>> send,
        IRateLimiter rateLimiter,
        int maxRetries,
        string exhaustedMessage,
        Action<int, TimeSpan> onRateLimited,
        Action<int, int, TimeSpan> onServerError
    )
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            await rateLimiter.WaitAsync();

            var response = await send();

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                var delay = RetryBackoff.Exponential(attempt);
                onRateLimited(attempt, delay);
                rateLimiter.PauseFor(delay);
                response.Dispose();
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < maxRetries)
            {
                var delay = RetryBackoff.Exponential(attempt);
                onServerError((int)response.StatusCode, attempt, delay);
                response.Dispose();
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return response;
        }

        throw new HttpRequestException(exhaustedMessage);
    }
}
