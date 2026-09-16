using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Equibles.Integrations.Common.Retry;
using Equibles.Integrations.DelayedTrades;

namespace Equibles.Integrations.Euronext.DelayedTrades;

// The post-trade file Euronext publishes per location under MiFIR article 13; one zip per session and window.
public class EuronextDelayedTradeSource(HttpClient httpClient) : IDelayedTradeSource
{
    public const string Origin = "https://marketdata.euronext.com";
    public const int MaxZipBytes = 32 * 1024 * 1024;
    private const int MaxRetries = 3;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public string SourceKey => EuronextDelayedTradeTerms.SourceKey;
    public DelayedTradeAttribution Attribution => EuronextDelayedTradeTerms.Attribution;

    public static Uri FileUrl(string locationCode, DelayedTradeWindow window)
    {
        if (
            string.IsNullOrWhiteSpace(locationCode)
            || locationCode.Length > 8
            || !locationCode.All(char.IsAsciiLetterUpper)
        )
            throw new ArgumentException("A Euronext location code is a short upper-case token.");
        var segment = window switch
        {
            DelayedTradeWindow.CurrentSession => "CURRENT_TRADING_DAY",
            DelayedTradeWindow.PreviousSession => "PREVIOUS_TRADING_DAY",
            _ => throw new ArgumentOutOfRangeException(nameof(window)),
        };
        return new Uri(
            $"{Origin}/data-reporting-service/trades-file/download/EQUITIES/{segment}/{locationCode}"
        );
    }

    public async Task<DelayedTradeFile> Fetch(
        string locationCode,
        DelayedTradeWindow window,
        CancellationToken cancellationToken
    )
    {
        var url = FileUrl(locationCode, window);
        for (var attempt = 0; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );
            var fetchedAt = DateTime.UtcNow;
            // A missing file is an answer (no session yet, or a holiday), not a fault to retry.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new DelayedTradeFile(
                    SourceKey,
                    locationCode,
                    window,
                    DelayedTradeFetchOutcome.NoSession,
                    url.ToString(),
                    EuronextDelayedTradeTerms.TermsUrl,
                    null,
                    0,
                    fetchedAt,
                    null
                );
            if (
                (
                    response.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500
                )
                && attempt < MaxRetries
            )
            {
                await Task.Delay(RetryBackoff.Exponential(attempt), cancellationToken);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (
                response.RequestMessage?.RequestUri is { } actual
                && (
                    actual.Scheme != "https"
                    || actual.Host != "marketdata.euronext.com"
                    || !actual.IsDefaultPort
                )
            )
                throw new InvalidDataException("Euronext response left its official HTTPS origin.");
            if (response.Content.Headers.ContentLength > MaxZipBytes)
                throw new InvalidDataException("Euronext trades file exceeds the capture limit.");
            var content = await ReadBounded(response, timeout.Token);
            return new DelayedTradeFile(
                SourceKey,
                locationCode,
                window,
                DelayedTradeFetchOutcome.Served,
                url.ToString(),
                EuronextDelayedTradeTerms.TermsUrl,
                Convert.ToHexStringLower(SHA256.HashData(content)),
                content.Length,
                fetchedAt,
                content
            );
        }
    }

    public IEnumerable<DelayedTradePrint> Parse(
        DelayedTradeFile file,
        DelayedTradeParseCounters counters
    )
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Outcome != DelayedTradeFetchOutcome.Served || file.Content == null)
            return [];
        return EuronextTradesFileParser.Read(file.Content, counters);
    }

    private static async Task<byte[]> ReadBounded(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaxZipBytes)
                throw new InvalidDataException("Euronext trades file exceeds the capture limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
