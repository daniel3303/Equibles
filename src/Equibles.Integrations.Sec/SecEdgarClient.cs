using System.Net;
using Newtonsoft.Json;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Extensions;
using Equibles.Integrations.Sec.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Sec;

[Service(ServiceLifetime.Scoped, typeof(ISecEdgarClient))]
public class SecEdgarClient : ISecEdgarClient {
    // SEC enforces 10 requests/second per User-Agent; use 8 to leave headroom for browser usage
    private static readonly IRateLimiter RateLimiter = new RateLimiter(maxRequests: 8, timeWindow: TimeSpan.FromSeconds(1));
    private const int MaxRetries = 10;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly ILogger<SecEdgarClient> _logger;
    private CachedResponse _cachedContent; // Used to cache the latest fetched list of documents
    private const string BaseUrl = "https://data.sec.gov";
    private const string FilesBaseUrl = "https://www.sec.gov";

    public SecEdgarClient(HttpClient httpClient, ILogger<SecEdgarClient> logger) {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Equibles Integration API/1.0 (contact@equibles.com)");
        _httpClient.Timeout = TimeSpan.FromMinutes(2);

    }

    public async Task<List<CompanyInfo>> GetActiveCompanies() {
        try {
            var url = $"{FilesBaseUrl}/files/company_tickers_exchange.json";
            _logger.LogInformation("Requesting: {Url}", url);

            using var response = await SendWithRetryAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var companiesResponse = JsonConvert.DeserializeObject<CompanyTickersResponse>(content);

            var companies = ParseCompaniesFromResponse(companiesResponse);

            _logger.LogInformation("Successfully retrieved {Count} active companies", companies.Count);
            return companies;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving active companies");
            throw;
        }
    }

    public async Task<string> GetEntityType(string cik) {
        try {
            var formattedCik = FormatCik(cik);
            var url = BuildUrl($"/submissions/CIK{formattedCik}.json");

            using var response = await SendWithRetryAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject<SecApiResponse>(content);

            return apiResponse?.EntityType;
        } catch (HttpRequestException ex) {
            // HTTP errors (including exhausted 429 retries) propagate to caller
            _logger.LogError(ex, "HTTP error retrieving entity type for CIK: {Cik}", cik);
            throw;
        } catch (Exception ex) {
            // Non-HTTP errors (deserialization, etc.) — return null as "not found"
            _logger.LogError(ex, "Error retrieving entity type for CIK: {Cik}", cik);
            return null;
        }
    }

    public async Task<List<FilingData>> GetCompanyFilings(string cik, DocumentTypeFilter? documentType = null, DateOnly? fromDate = null, DateOnly? toDate = null) {
        try {
            var formattedCik = FormatCik(cik);
            var url = BuildUrl($"/submissions/CIK{formattedCik}.json");

            string content;
            if (_cachedContent != null && _cachedContent.Url == url) {
                _logger.LogInformation("Using cached content for URL: {Url}", url);
                content = _cachedContent.Content;
            } else {
                using var response = await SendWithRetryAsync(url);
                response.EnsureSuccessStatusCode();

                content = await response.Content.ReadAsStringAsync();
                _cachedContent = new CachedResponse(url, content);
            }

            var apiResponse = JsonConvert.DeserializeObject<SecApiResponse>(content);

            var filings = MapToFilingData(apiResponse?.Filings?.Recent, cik);
            var filteredFilings = FilterFilings(filings, documentType, fromDate, toDate);

            _logger.LogInformation("Successfully retrieved {Count} filings for CIK: {Cik}", filteredFilings.Count,
                formattedCik);
            return filteredFilings;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving filings for CIK: {Cik}", cik);
            throw;
        }
    }

    public async Task<string> GetDocumentContent(FilingData filing) {
        if (filing == null)
            throw new ArgumentNullException(nameof(filing), "Filing data cannot be null");

        if (string.IsNullOrEmpty(filing.AccessionNumber) || string.IsNullOrEmpty(filing.Cik))
            throw new ArgumentException("Filing data must contain valid AccessionNumber and Cik");

        return await GetDocumentContent(filing.AccessionNumber, filing.Cik);
    }

    public async Task<string> GetDocumentContent(string accessionNumber, string cik) {
        try {
            var url = GetDocumentUrl(cik, accessionNumber);

            _logger.LogInformation("Requesting document: {Url}", url);

            using var response = await SendWithRetryAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Successfully retrieved document content ({Length} characters)", content.Length);
            return content;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving document content for accession: {AccessionNumber}, CIK: {Cik}",
                accessionNumber, cik);
            throw;
        }
    }


    public async Task<Stream> DownloadStream(string url) {
        var response = await SendWithRetryAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url) {
        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            await RateLimiter.WaitAsync();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.GetAsync(url);
            sw.Stop();

            _logger.LogDebug("SEC request {StatusCode} {Elapsed}ms {Url}",
                (int)response.StatusCode, sw.ElapsedMilliseconds, url);

            if (response.StatusCode == HttpStatusCode.TooManyRequests) {
                var delay = GetRetryDelay(response, attempt);

                _logger.LogWarning(
                    "SEC EDGAR rate limited (429) for {Url}, pausing for {Delay}s (attempt {Attempt}/{Max})",
                    url, delay.TotalSeconds, attempt + 1, MaxRetries);

                RateLimiter.PauseFor(delay);

                if (attempt < MaxRetries) {
                    response.Dispose();
                    await Task.Delay(delay);
                    continue;
                }
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries) {
                var delay = GetRetryDelay(response, attempt);

                _logger.LogWarning(
                    "SEC EDGAR server error ({StatusCode}) for {Url}, retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode, url, delay.TotalSeconds, attempt + 1, MaxRetries);

                response.Dispose();
                await Task.Delay(delay);
                continue;
            }

            return response;
        }

        throw new HttpRequestException($"Max retries ({MaxRetries}) exceeded for SEC EDGAR request: {url}");
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt) {
        if (response.Headers.RetryAfter?.Delta is { } delta) {
            return delta > MaxRetryDelay ? MaxRetryDelay : delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date) {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) {
                return wait > MaxRetryDelay ? MaxRetryDelay : wait;
            }
        }

        // Exponential backoff: 2s, 4s, 8s, 16s, 32s — capped at 1 min
        var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
        return backoff > MaxRetryDelay ? MaxRetryDelay : backoff;
    }

    private static List<CompanyInfo> ParseCompaniesFromResponse(CompanyTickersResponse response) {
        if (response?.Fields == null || response.Data == null)
            return [];

        // Expected fields: ["cik","name","ticker","exchange"]
        var cikIndex = response.Fields.IndexOf("cik");
        var nameIndex = response.Fields.IndexOf("name");
        var tickerIndex = response.Fields.IndexOf("ticker");

        if (cikIndex == -1 || nameIndex == -1 || tickerIndex == -1)
            return [];

        // Group rows by CIK — the SEC file has one row per ticker,
        // so companies with multiple tickers appear as multiple rows.
        // The first ticker per CIK is the primary.
        var companiesByCik = new Dictionary<string, CompanyInfo>();

        foreach (var row in response.Data) {
            if (row.Count <= Math.Max(Math.Max(cikIndex, nameIndex), tickerIndex))
                continue;

            var cik = row[cikIndex]?.ToString();
            var name = row[nameIndex]?.ToString();
            var ticker = row[tickerIndex]?.ToString();

            if (string.IsNullOrEmpty(cik) || string.IsNullOrEmpty(ticker))
                continue;

            if (companiesByCik.TryGetValue(cik, out var existing)) {
                existing.Tickers.Add(ticker);
            } else {
                companiesByCik[cik] = new CompanyInfo {
                    Cik = cik,
                    Name = name ?? string.Empty,
                    Tickers = [ticker]
                };
            }
        }

        return companiesByCik.Values.ToList();
    }

    private static List<FilingData> MapToFilingData(RecentFilings recent, string cik) {
        if (recent == null || recent.AccessionNumber.Count == 0)
            return [];

        var filings = new List<FilingData>();

        for (var i = 0; i < recent.AccessionNumber.Count; i++) {
            var accessionNumber = recent.AccessionNumber[i];
            filings.Add(new FilingData {
                Cik = cik,
                AccessionNumber = accessionNumber,
                FilingDate = DateOnly.TryParse(recent.FilingDate[i], out var fd) ? fd : DateOnly.MinValue,
                ReportDate = DateOnly.TryParse(recent.ReportDate[i], out var rd) ? rd : DateOnly.MinValue,
                Form = recent.Form[i],
                PrimaryDocument = recent.PrimaryDocument[i],
                Description = recent.PrimaryDocDescription[i],
                DocumentUrl = GetDocumentUrl(cik, accessionNumber)
            });
        }

        return filings;
    }

    private static List<FilingData> FilterFilings(List<FilingData> filings, DocumentTypeFilter? documentType,
        DateOnly? fromDate, DateOnly? toDate
    ) {
        return filings.Where(f =>
            (!documentType.HasValue || f.Form == documentType.Value.GetFormName()) &&
            (!fromDate.HasValue || f.FilingDate >= fromDate.Value) &&
            (!toDate.HasValue || f.FilingDate <= toDate.Value)
        ).ToList();
    }

    private static string GetDocumentUrl(string cik, string accessionNumber) {
        if (string.IsNullOrEmpty(cik) || string.IsNullOrEmpty(accessionNumber)) {
            return string.Empty;
        }

        var formattedCik = FormatCik(cik);

        // Official SEC pattern for raw text files
        return $"https://www.sec.gov/Archives/edgar/data/{formattedCik}/{accessionNumber}.txt";
    }

    private static string FormatCik(string cik) {
        return cik.PadLeft(10, '0');
    }

    private static string BuildUrl(string endpoint) {
        return $"{BaseUrl}{endpoint}";
    }

    private record CachedResponse(string Url, string Content);
}