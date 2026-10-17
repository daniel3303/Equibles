using System.Text;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Gleif;

// The one GLEIF origin, pace and bounded read every GLEIF client shares, so two clients never double the rate.
internal static class GleifHttp
{
    private const int MaxResponseBytes = 2_000_000;
    private const int MaxRetries = 3;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

    // GLEIF throttled an unpaced sequential pass from about 240 requests a minute; the typed clients are
    // transient, so the pace is shared statically, as FRED's is.
    internal const int RequestsPerMinute = 60;

    internal static readonly Uri Origin = new("https://api.gleif.org");

    private static readonly IRateLimiter Pace = new Common.RateLimiter.RateLimiter(
        RequestsPerMinute,
        TimeSpan.FromMinutes(1)
    );

    internal static async Task<string> Read(
        HttpClient httpClient,
        Uri uri,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        // HttpClient.Timeout stops at the headers; this also bounds a stalled body.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        var token = timeout.Token;
        using var response = await HttpRetry.Send(
            () =>
                httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, uri),
                    HttpCompletionOption.ResponseHeadersRead,
                    token
                ),
            Pace,
            MaxRetries,
            "GLEIF request retries exhausted.",
            (attempt, delay) =>
                logger.LogWarning(
                    "GLEIF rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                ),
            (statusCode, attempt, delay) =>
                logger.LogWarning(
                    "GLEIF server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    statusCode,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                ),
            token
        );
        if (response.RequestMessage?.RequestUri is { } actual && actual != uri)
            throw new InvalidDataException("GLEIF response was redirected.");
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new InvalidDataException("GLEIF response exceeds its capture limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[16_384];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("GLEIF response exceeds its capture limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }
}
