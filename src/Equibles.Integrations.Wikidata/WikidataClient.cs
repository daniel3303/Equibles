using System.Net;
using System.Text.RegularExpressions;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using Equibles.Integrations.Wikidata.Contracts;
using Equibles.Integrations.Wikidata.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Equibles.Integrations.Wikidata;

/// <summary>
/// Reads company facts from the Wikidata SPARQL endpoint. Wikidata stores the
/// SEC CIK (property P5531) zero-padded to 10 digits and the Legal Entity
/// Identifier (property P1278) as issued, which makes both exact-match join
/// keys, no ticker or name fuzziness.
/// </summary>
[Service(ServiceLifetime.Scoped, typeof(IWikidataClient))]
public partial class WikidataClient : IWikidataClient
{
    private const string Endpoint = "https://query.wikidata.org/sparql";

    // The Wikidata query service requires a descriptive User-Agent with a
    // contact address; anonymous clients get throttled or blocked.
    private const string UserAgent = "EquiblesBot/1.0 (+https://equibles.com)";

    private const string CikProperty = "P5531";
    private const string LeiProperty = "P1278";

    // Keys per SPARQL VALUES clause. Bounded so the GET URL stays well under
    // length limits and a single query stays cheap for the endpoint.
    private const int ChunkSize = 200;

    private const int PaddedCikLength = 10;

    // Half of the documented WDQS allowance, by construction: the service grants
    // each client (user agent + IP) 60 seconds of query processing time per
    // 60-second window (https://www.mediawiki.org/wiki/Wikidata_Query_Service/User_Manual,
    // "Query limits"). Our bounded VALUES queries cost well under 1 second of
    // processing each and run sequentially, so 30 requests per 60 seconds consumes
    // at most 30 of the allowed 60 processing-seconds.
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 30,
        timeWindow: TimeSpan.FromSeconds(60)
    );

    // WDQS answers 429 (with Retry-After) and 502/503/504 while its backends are overloaded.
    // Two retries ride out a brief blip; a longer outage is left to the caller's next cycle.
    private const int MaxAttempts = 3;

    // A Retry-After beyond this is not waited out inside one discovery batch.
    private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly ILogger<WikidataClient> _logger;

    public WikidataClient(HttpClient httpClient, ILogger<WikidataClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Seams for tests: the shared limiter and the real clock are process-wide.
    internal IRateLimiter Limiter { get; init; } = RateLimiter;

    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public Task<IReadOnlyDictionary<string, string>> GetOfficialWebsitesByCik(
        IReadOnlyCollection<string> ciks,
        CancellationToken cancellationToken
    )
    {
        // Padded → as-passed, so results key back to the caller's format. Only
        // digit-shaped CIKs are queryable (and safe to inline in the query).
        var queryKeyToOriginal = new Dictionary<string, string>();
        foreach (var cik in ciks)
        {
            var trimmed = cik?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && trimmed.All(char.IsAsciiDigit))
                queryKeyToOriginal.TryAdd(trimmed.PadLeft(PaddedCikLength, '0'), cik);
        }
        return ResolveWebsites(CikProperty, "CIKs", queryKeyToOriginal, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, string>> GetOfficialWebsitesByLei(
        IReadOnlyCollection<string> leis,
        CancellationToken cancellationToken
    )
    {
        // An LEI is 20 upper-case alphanumerics (ISO 17442); anything else is neither a
        // Wikidata key nor safe to inline in the query.
        var queryKeyToOriginal = new Dictionary<string, string>();
        foreach (var lei in leis)
        {
            var trimmed = lei?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && LeiShape().IsMatch(trimmed))
                queryKeyToOriginal.TryAdd(trimmed, lei);
        }
        return ResolveWebsites(LeiProperty, "LEIs", queryKeyToOriginal, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveWebsites(
        string property,
        string identifierLabel,
        Dictionary<string, string> queryKeyToOriginal,
        CancellationToken cancellationToken
    )
    {
        var websites = new Dictionary<string, string>();
        foreach (var chunk in queryKeyToOriginal.Keys.Chunk(ChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var binding in await Query(property, chunk, cancellationToken))
            {
                var key = binding.Key?.Value;
                var website = binding.Website?.Value;
                if (key == null || string.IsNullOrWhiteSpace(website))
                    continue;
                if (!queryKeyToOriginal.TryGetValue(key, out var original))
                    continue;

                // P856 often holds many localised variants (apple.com/de/, …);
                // the shortest URL is the canonical root. Ordinal tie-break keeps
                // the pick deterministic.
                if (
                    !websites.TryGetValue(original, out var current)
                    || website.Length < current.Length
                    || (
                        website.Length == current.Length
                        && string.CompareOrdinal(website, current) < 0
                    )
                )
                    websites[original] = website;
            }
        }

        _logger.LogDebug(
            "Wikidata resolved websites for {Found} of {Requested} {Identifier}",
            websites.Count,
            queryKeyToOriginal.Count,
            identifierLabel
        );
        return websites;
    }

    private async Task<List<SparqlBinding>> Query(
        string property,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken
    )
    {
        var values = string.Join(' ', keys.Select(key => $"\"{key}\""));
        var sparql =
            "SELECT ?key ?website WHERE { "
            + $"VALUES ?key {{ {values} }} "
            + $"?item wdt:{property} ?key ; wdt:P856 ?website . }}";

        var url = $"{Endpoint}?query={Uri.EscapeDataString(sparql)}";
        for (var attempt = 1; ; attempt++)
        {
            await Limiter.WaitAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/sparql-results+json");
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (IsTransient(response.StatusCode))
            {
                var wait = RetryWait(response, attempt);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    Limiter.PauseFor(wait);
                if (attempt >= MaxAttempts || wait > MaxRetryWait)
                    throw new WikidataUnavailableException(
                        response.StatusCode,
                        $"Wikidata query service answered {(int)response.StatusCode} after "
                            + $"{attempt} attempt(s); retry after {wait.TotalSeconds:0}s"
                    );

                _logger.LogWarning(
                    "Wikidata answered {Status}; retrying in {Delay} (attempt {Attempt} of {Max})",
                    (int)response.StatusCode,
                    wait,
                    attempt,
                    MaxAttempts
                );
                await Delay(wait, cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonConvert.DeserializeObject<SparqlResultsResponse>(json);
            return parsed?.Results?.Bindings ?? [];
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status
            is HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;

    // The service's own Retry-After wins; without one, the shared exponential backoff applies.
    private static TimeSpan RetryWait(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retryAfter?.Date is { } date)
        {
            var untilDate = date - DateTimeOffset.UtcNow;
            return untilDate < TimeSpan.Zero ? TimeSpan.Zero : untilDate;
        }
        return RetryBackoff.Exponential(attempt - 1);
    }

    [GeneratedRegex("^[A-Z0-9]{20}$")]
    private static partial Regex LeiShape();
}
