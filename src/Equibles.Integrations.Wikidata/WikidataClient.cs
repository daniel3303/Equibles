using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Wikidata.Contracts;
using Equibles.Integrations.Wikidata.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Equibles.Integrations.Wikidata;

/// <summary>
/// Reads company facts from the Wikidata SPARQL endpoint. Wikidata stores the
/// SEC CIK (property P5531) zero-padded to 10 digits, which makes it an
/// exact-match join key — no ticker or name fuzziness.
/// </summary>
[Service(ServiceLifetime.Scoped, typeof(IWikidataClient))]
public class WikidataClient : IWikidataClient
{
    private const string Endpoint = "https://query.wikidata.org/sparql";

    // The Wikidata query service requires a descriptive User-Agent with a
    // contact address; anonymous clients get throttled or blocked.
    private const string UserAgent = "EquiblesBot/1.0 (+https://equibles.com)";

    // CIKs per SPARQL VALUES clause. Bounded so the GET URL stays well under
    // length limits and a single query stays cheap for the endpoint.
    private const int ChunkSize = 200;

    private const int PaddedCikLength = 10;

    // Polite shared throttle: WDQS is a shared public service.
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 1,
        timeWindow: TimeSpan.FromSeconds(1)
    );

    private readonly HttpClient _httpClient;
    private readonly ILogger<WikidataClient> _logger;

    public WikidataClient(HttpClient httpClient, ILogger<WikidataClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetOfficialWebsitesByCik(
        IReadOnlyCollection<string> ciks,
        CancellationToken cancellationToken
    )
    {
        // Padded → as-passed, so results key back to the caller's format. Only
        // digit-shaped CIKs are queryable (and safe to inline in the query).
        var paddedToOriginal = new Dictionary<string, string>();
        foreach (var cik in ciks)
        {
            var trimmed = cik?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && trimmed.All(char.IsAsciiDigit))
                paddedToOriginal.TryAdd(trimmed.PadLeft(PaddedCikLength, '0'), cik);
        }

        var websites = new Dictionary<string, string>();
        foreach (var chunk in paddedToOriginal.Keys.Chunk(ChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var binding in await Query(chunk, cancellationToken))
            {
                var paddedCik = binding.Cik?.Value;
                var website = binding.Website?.Value;
                if (paddedCik == null || string.IsNullOrWhiteSpace(website))
                    continue;
                if (!paddedToOriginal.TryGetValue(paddedCik, out var originalCik))
                    continue;

                // P856 often holds many localised variants (apple.com/de/, …);
                // the shortest URL is the canonical root. Ordinal tie-break keeps
                // the pick deterministic.
                if (
                    !websites.TryGetValue(originalCik, out var current)
                    || website.Length < current.Length
                    || (
                        website.Length == current.Length
                        && string.CompareOrdinal(website, current) < 0
                    )
                )
                    websites[originalCik] = website;
            }
        }

        _logger.LogDebug(
            "Wikidata resolved websites for {Found} of {Requested} CIKs",
            websites.Count,
            paddedToOriginal.Count
        );
        return websites;
    }

    private async Task<List<SparqlBinding>> Query(
        IReadOnlyCollection<string> paddedCiks,
        CancellationToken cancellationToken
    )
    {
        var values = string.Join(' ', paddedCiks.Select(cik => $"\"{cik}\""));
        var sparql =
            "SELECT ?cik ?website WHERE { "
            + $"VALUES ?cik {{ {values} }} "
            + "?item wdt:P5531 ?cik ; wdt:P856 ?website . }";

        await RateLimiter.WaitAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Endpoint}?query={Uri.EscapeDataString(sparql)}"
        );
        request.Headers.Accept.ParseAdd("application/sparql-results+json");
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonConvert.DeserializeObject<SparqlResultsResponse>(json);
        return parsed?.Results?.Bindings ?? [];
    }
}
