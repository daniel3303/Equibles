using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Extensions;
using Equibles.Integrations.Sec.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Equibles.Integrations.Sec;

[Service(ServiceLifetime.Scoped, typeof(ISecEdgarClient))]
public class SecEdgarClient : ISecEdgarClient
{
    // SEC has undocumented rolling-window rate limits beyond the 10 req/s rule; use 4 req/s for sustained scraping
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 4,
        timeWindow: TimeSpan.FromSeconds(1)
    );
    private const int MaxRetries = 10;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly ILogger<SecEdgarClient> _logger;
    private CachedResponse _cachedContent; // Used to cache the latest fetched list of documents
    private const string BaseUrl = "https://data.sec.gov";
    private const string FilesBaseUrl = "https://www.sec.gov";

    public SecEdgarClient(
        HttpClient httpClient,
        ILogger<SecEdgarClient> logger,
        IConfiguration configuration
    )
    {
        _httpClient = httpClient;
        _logger = logger;

        var contactEmail = configuration["Sec:ContactEmail"];
        if (!string.IsNullOrWhiteSpace(contactEmail))
        {
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                $"Equibles Open Source/1.0 ({contactEmail})"
            );
        }
        else
        {
            _logger.LogWarning(
                "Sec:ContactEmail not configured — SEC EDGAR requests will be blocked (403). "
                    + "Set SEC_CONTACT_EMAIL in your .env file."
            );
        }

        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<List<CompanyInfo>> GetActiveCompanies()
    {
        try
        {
            var url = $"{FilesBaseUrl}/files/company_tickers_exchange.json";
            _logger.LogInformation("Requesting: {Url}", url);

            using var response = await SendWithRetryAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var companiesResponse = JsonConvert.DeserializeObject<CompanyTickersResponse>(content);

            var companies = ParseCompaniesFromResponse(companiesResponse);

            _logger.LogInformation(
                "Successfully retrieved {Count} active companies",
                companies.Count
            );
            return companies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active companies");
            throw;
        }
    }

    public async Task<string> GetEntityType(string cik)
    {
        var metadata = await GetCompanyMetadata(cik);
        return metadata?.EntityType;
    }

    public async Task<CompanyMetadata> GetCompanyMetadata(string cik)
    {
        try
        {
            var formattedCik = FormatCik(cik);
            var url = BuildUrl($"/submissions/CIK{formattedCik}.json");

            using var response = await SendWithRetryAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject<SecApiResponse>(content);

            if (apiResponse == null)
                return null;

            // Cache the submissions payload keyed by URL so an immediately
            // following GetCompanyFilings(cik) — which hits the same
            // /submissions/CIK*.json URL — reuses it instead of re-fetching.
            // This keeps fiscal-year detection in the scraper net-zero extra
            // SEC requests on the common path.
            _cachedContent = new CachedResponse(url, content);

            return new CompanyMetadata
            {
                Cik = cik,
                EntityType = apiResponse.EntityType,
                Exchanges = apiResponse.Exchanges ?? [],
                FiscalYearEnd = apiResponse.FiscalYearEnd,
            };
        }
        catch (HttpRequestException ex)
        {
            // HTTP errors (including exhausted 429 retries) propagate to caller
            _logger.LogError(ex, "HTTP error retrieving metadata for CIK: {Cik}", cik);
            throw;
        }
        catch (Exception ex)
        {
            // Non-HTTP errors (deserialization, etc.) — return null as "not found"
            _logger.LogError(ex, "Error retrieving metadata for CIK: {Cik}", cik);
            return null;
        }
    }

    public async Task<CompanyFactsResponse> GetCompanyFacts(string cik)
    {
        try
        {
            var formattedCik = FormatCik(cik);
            var url = BuildUrl($"/api/xbrl/companyfacts/CIK{formattedCik}.json");

            using var response = await SendWithRetryAsync(url);

            // Companies with no XBRL facts return 404 — a normal "nothing to
            // ingest" outcome, not an error.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<CompanyFactsResponse>(content);
        }
        catch (HttpRequestException ex)
        {
            // HTTP errors (including exhausted 429 retries) propagate to caller
            _logger.LogError(ex, "HTTP error retrieving company facts for CIK: {Cik}", cik);
            throw;
        }
        catch (Exception ex)
        {
            // Non-HTTP errors (deserialization, etc.) — return null as "not found"
            _logger.LogError(ex, "Error retrieving company facts for CIK: {Cik}", cik);
            return null;
        }
    }

    public async Task<List<FilingData>> GetCompanyFilings(
        string cik,
        DocumentTypeFilter? documentType = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null
    )
    {
        try
        {
            var formattedCik = FormatCik(cik);
            var url = BuildUrl($"/submissions/CIK{formattedCik}.json");

            string content;
            if (_cachedContent != null && _cachedContent.Url == url)
            {
                _logger.LogInformation("Using cached content for URL: {Url}", url);
                content = _cachedContent.Content;
            }
            else
            {
                using var response = await SendWithRetryAsync(url);
                response.EnsureSuccessStatusCode();

                content = await response.Content.ReadAsStringAsync();
                _cachedContent = new CachedResponse(url, content);
            }

            var apiResponse = JsonConvert.DeserializeObject<SecApiResponse>(content);

            var filings = MapToFilingData(apiResponse?.Filings?.Recent, cik);

            // Fetch older filings from archive files (SEC paginates older filings into separate JSON files)
            var archiveFilings = await GetArchiveFilings(
                apiResponse?.Filings,
                cik,
                fromDate,
                toDate
            );
            filings.AddRange(archiveFilings);

            // Deduplicate in case recent and archive ranges overlap
            filings = filings.DistinctBy(f => f.AccessionNumber).ToList();

            var filteredFilings = FilterFilings(filings, documentType, fromDate, toDate);

            _logger.LogInformation(
                "Successfully retrieved {Count} filings for CIK: {Cik}",
                filteredFilings.Count,
                formattedCik
            );
            return filteredFilings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving filings for CIK: {Cik}", cik);
            throw;
        }
    }

    private async Task<List<FilingData>> GetArchiveFilings(
        FilingsContainer filings,
        string cik,
        DateOnly? fromDate,
        DateOnly? toDate
    )
    {
        if (filings?.Files == null || filings.Files.Count == 0)
            return [];

        var result = new List<FilingData>();

        foreach (var archiveFile in filings.Files)
        {
            // Skip archive files whose date range is entirely outside the requested window
            if (
                fromDate.HasValue
                && DateOnly.TryParse(archiveFile.FilingTo, out var archiveTo)
                && archiveTo < fromDate.Value
            )
            {
                _logger.LogDebug(
                    "Skipping archive {File} — all filings before {FromDate}",
                    archiveFile.Name,
                    fromDate
                );
                continue;
            }

            if (
                toDate.HasValue
                && DateOnly.TryParse(archiveFile.FilingFrom, out var archiveFrom)
                && archiveFrom > toDate.Value
            )
            {
                _logger.LogDebug(
                    "Skipping archive {File} — all filings after {ToDate}",
                    archiveFile.Name,
                    toDate
                );
                continue;
            }

            var archiveUrl = BuildUrl($"/submissions/{archiveFile.Name}");
            _logger.LogInformation(
                "Fetching archive filings from {File} ({Count} filings)",
                archiveFile.Name,
                archiveFile.FilingCount
            );

            using var response = await SendWithRetryAsync(archiveUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var archiveFilings = JsonConvert.DeserializeObject<RecentFilings>(content);
            result.AddRange(MapToFilingData(archiveFilings, cik));
        }

        return result;
    }

    public async Task<string> GetDocumentContent(FilingData filing)
    {
        if (filing == null)
            throw new ArgumentNullException(nameof(filing), "Filing data cannot be null");

        if (string.IsNullOrEmpty(filing.AccessionNumber) || string.IsNullOrEmpty(filing.Cik))
            throw new ArgumentException("Filing data must contain valid AccessionNumber and Cik");

        return await GetDocumentContent(filing.AccessionNumber, filing.Cik);
    }

    public async Task<string> GetDocumentContent(string accessionNumber, string cik)
    {
        try
        {
            var url = GetDocumentUrl(cik, accessionNumber);

            _logger.LogInformation("Requesting document: {Url}", url);

            using var response = await SendWithRetryAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInformation(
                "Successfully retrieved document content ({Length} characters)",
                content.Length
            );
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving document content for accession: {AccessionNumber}, CIK: {Cik}",
                accessionNumber,
                cik
            );
            throw;
        }
    }

    public async Task<byte[]> GetDocumentFileBytes(
        string cik,
        string accessionNumber,
        string filename,
        CancellationToken cancellationToken = default
    )
    {
        if (
            string.IsNullOrEmpty(cik)
            || string.IsNullOrEmpty(accessionNumber)
            || string.IsNullOrEmpty(filename)
        )
            throw new ArgumentException("cik, accessionNumber and filename are required");

        var url = BuildArchiveUrl(cik, accessionNumber, Uri.EscapeDataString(filename));

        _logger.LogInformation("Requesting filing artifact: {Url}", url);

        using var response = await SendWithRetryAsync(url, cancellationToken);

        // 404 means the parsed filename does not match a published artifact (renamed, case
        // mismatch, etc.). Return empty so the caller can warn-and-skip rather than retry-loop.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Filing artifact not found at {Url}", url);
            return [];
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<Stream> DownloadStream(string url)
    {
        var response = await SendWithRetryAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    public async Task<List<EdgarDailyIndexEntry>> GetDailyIndex(
        DateOnly date,
        CancellationToken cancellationToken = default
    )
    {
        var quarter = (date.Month - 1) / 3 + 1;

        // Use the pipe-delimited master index, not the space-padded form.idx:
        // company names contain spaces and CIK is right-aligned in the legacy
        // fixed-width layout, so column-offset parsing is fragile. The master
        // index is unambiguous: CIK|Company Name|Form Type|Date Filed|File Name.
        var url =
            $"{FilesBaseUrl}/Archives/edgar/daily-index/{date.Year}/QTR{quarter}/master.{date:yyyyMMdd}.idx";

        using var response = await SendWithRetryAsync(url, cancellationToken);

        // No index file for that day → nothing to ingest, skip it rather than
        // failing the whole real-time sweep. Weekends/holidays can 404, and SEC
        // returns 403 (Forbidden) for not-yet-published / future-dated index
        // files (e.g. "today" before the daily index is posted). Both mean the
        // same thing here: there is no daily index for this date.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            _logger.LogInformation("No daily index published for {Date:yyyy-MM-dd}", date);
            return [];
        }

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return ParseMasterIndex(content, date);
    }

    /// <summary>
    /// Parses the pipe-delimited <c>master.idx</c> body
    /// (<c>CIK|Company Name|Form Type|Date Filed|File Name</c>), keeping only
    /// 13F-HR / 13F-HR/A rows with an all-digit CIK.
    /// </summary>
    private static List<EdgarDailyIndexEntry> ParseMasterIndex(
        string content,
        DateOnly fallbackDate
    )
    {
        var entries = new List<EdgarDailyIndexEntry>();

        foreach (var rawLine in content.Split('\n'))
        {
            if (TryParseMasterIndexLine(rawLine, fallbackDate, out var entry))
                entries.Add(entry);
        }

        return entries;
    }

    private static bool TryParseMasterIndexLine(
        string rawLine,
        DateOnly fallbackDate,
        out EdgarDailyIndexEntry entry
    )
    {
        entry = null;

        var line = rawLine.Trim();
        if (line.Length == 0)
            return false;

        var fields = line.Split('|');
        if (fields.Length < 5)
            return false;

        var cik = fields[0].Trim();
        var company = fields[1].Trim();
        var formType = fields[2].Trim();
        var dateFiled = fields[3].Trim();
        var fileName = fields[4].Trim();

        if (!formType.StartsWith("13F-HR", StringComparison.OrdinalIgnoreCase))
            return false;

        // Header/preamble rows ("CIK", "Company Name", dashes) fail this.
        if (cik.Length == 0 || !cik.All(char.IsDigit))
            return false;

        // edgar/data/{cik}/{accession-with-dashes}.txt → accession number
        var accession = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(accession))
            return false;

        entry = new EdgarDailyIndexEntry
        {
            FormType = formType,
            CompanyName = company,
            Cik = cik,
            DateFiled = DateOnly.TryParse(dateFiled, out var d) ? d : fallbackDate,
            AccessionNumber = accession,
        };
        return true;
    }

    public async Task<List<string>> GetFilingArtifactNames(
        string cik,
        string accessionNumber,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(cik) || string.IsNullOrEmpty(accessionNumber))
            throw new ArgumentException("cik and accessionNumber are required");

        var url = BuildArchiveUrl(cik, accessionNumber, "index.json");

        using var response = await SendWithRetryAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Filing index not found at {Url}", url);
            return [];
        }

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var index = JsonConvert.DeserializeObject<FilingIndexResponse>(content);

        return index
                ?.Directory?.Item?.Where(item => !string.IsNullOrEmpty(item.Name))
                .Select(item => item.Name)
                .ToList()
            ?? [];
    }

    private Task<HttpResponseMessage> SendWithRetryAsync(
        string url,
        CancellationToken cancellationToken = default
    ) => SendWithRetryAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string url,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken = default
    )
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(url, completionOption, cancellationToken);
            }
            catch (HttpRequestException ex)
                when (IsTransientNetworkError(ex) && attempt < MaxRetries)
            {
                // DNS / socket / TLS failures (e.g. transient connectivity to www.sec.gov) are
                // retryable just like a 5xx — retry with backoff instead of surfacing them as
                // dashboard errors. Only a sustained outage exhausts retries and throws once.
                var delay = TransientBackoff(attempt);
                _logger.LogWarning(
                    ex,
                    "Transient network error reaching SEC EDGAR for {Url}, retrying in {Delay}s (attempt {Attempt}/{Max})",
                    url,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );
                await Task.Delay(delay, cancellationToken);
                continue;
            }
            sw.Stop();

            _logger.LogDebug(
                "SEC request {StatusCode} {Elapsed}ms {Url}",
                (int)response.StatusCode,
                sw.ElapsedMilliseconds,
                url
            );

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = GetRetryDelay(response, attempt);

                _logger.LogWarning(
                    "SEC EDGAR rate limited (429) for {Url}, pausing for {Delay}s (attempt {Attempt}/{Max})",
                    url,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );

                RateLimiter.PauseFor(delay);

                if (attempt < MaxRetries)
                {
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
            {
                var delay = GetRetryDelay(response, attempt);

                _logger.LogWarning(
                    "SEC EDGAR server error ({StatusCode}) for {Url}, retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode,
                    url,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
        }

        throw new HttpRequestException(
            $"Max retries ({MaxRetries}) exceeded for SEC EDGAR request: {url}"
        );
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta > MaxRetryDelay ? MaxRetryDelay : delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait > MaxRetryDelay ? MaxRetryDelay : wait;
            }
        }

        return TransientBackoff(attempt);
    }

    // Exponential backoff: 2s, 4s, 8s, 16s, 32s — capped at MaxRetryDelay.
    private static TimeSpan TransientBackoff(int attempt)
    {
        var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
        return backoff > MaxRetryDelay ? MaxRetryDelay : backoff;
    }

    // DNS resolution, socket and TLS handshake failures are transient connectivity problems,
    // not defects — they are retried, never recorded as dashboard errors.
    private static bool IsTransientNetworkError(HttpRequestException exception) =>
        exception.InnerException is SocketException or IOException or AuthenticationException;

    private static List<CompanyInfo> ParseCompaniesFromResponse(CompanyTickersResponse response)
    {
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

        foreach (var row in response.Data)
        {
            if (
                !TryExtractCompanyRow(
                    row,
                    cikIndex,
                    nameIndex,
                    tickerIndex,
                    out var cik,
                    out var name,
                    out var ticker
                )
            )
                continue;

            if (companiesByCik.TryGetValue(cik, out var existing))
            {
                existing.Tickers.Add(ticker);
            }
            else
            {
                companiesByCik[cik] = new CompanyInfo
                {
                    Cik = cik,
                    Name = name ?? string.Empty,
                    Tickers = [ticker],
                };
            }
        }

        return companiesByCik.Values.ToList();
    }

    private static bool TryExtractCompanyRow(
        List<object> row,
        int cikIndex,
        int nameIndex,
        int tickerIndex,
        out string cik,
        out string name,
        out string ticker
    )
    {
        cik = null;
        name = null;
        ticker = null;

        if (row.Count <= Math.Max(Math.Max(cikIndex, nameIndex), tickerIndex))
            return false;

        cik = row[cikIndex]?.ToString();
        name = row[nameIndex]?.ToString();
        ticker = row[tickerIndex]?.ToString();

        if (string.IsNullOrEmpty(cik) || string.IsNullOrEmpty(ticker))
            return false;

        return true;
    }

    private static List<FilingData> MapToFilingData(RecentFilings recent, string cik)
    {
        if (recent == null || recent.AccessionNumber.Count == 0)
            return [];

        var filings = new List<FilingData>();

        for (var i = 0; i < recent.AccessionNumber.Count; i++)
        {
            // SEC can emit a ragged payload where a secondary array is shorter
            // than AccessionNumber. Skip rows that cannot be fully mapped rather
            // than throw, mirroring ParseCompaniesFromResponse's short-row guard.
            if (
                recent.FilingDate.Count <= i
                || recent.ReportDate.Count <= i
                || recent.Form.Count <= i
                || recent.PrimaryDocument.Count <= i
                || recent.PrimaryDocDescription.Count <= i
            )
                continue;

            var accessionNumber = recent.AccessionNumber[i];
            filings.Add(
                new FilingData
                {
                    Cik = cik,
                    AccessionNumber = accessionNumber,
                    FilingDate = ParseInvariantDateOr(recent.FilingDate[i], DateOnly.MinValue),
                    ReportDate = ParseInvariantDateOr(recent.ReportDate[i], DateOnly.MinValue),
                    Form = recent.Form[i],
                    PrimaryDocument = recent.PrimaryDocument[i],
                    Description = recent.PrimaryDocDescription[i],
                    DocumentUrl = GetDocumentUrl(cik, accessionNumber),
                }
            );
        }

        return filings;
    }

    // SEC submissions feed dates are ISO yyyy-MM-dd. Parse them culture-independently —
    // under a non-Gregorian host culture (e.g. ar-SA Umm al-Qura) culture-sensitive
    // TryParse fails and every filing would be stamped with the fallback.
    private static DateOnly ParseInvariantDateOr(string text, DateOnly fallback) =>
        DateOnly.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed
        )
            ? parsed
            : fallback;

    private static List<FilingData> FilterFilings(
        List<FilingData> filings,
        DocumentTypeFilter? documentType,
        DateOnly? fromDate,
        DateOnly? toDate
    )
    {
        return filings
            .Where(f =>
                (!documentType.HasValue || f.Form == documentType.Value.GetFormName())
                && (!fromDate.HasValue || f.FilingDate >= fromDate.Value)
                && (!toDate.HasValue || f.FilingDate <= toDate.Value)
            )
            .ToList();
    }

    private static string GetDocumentUrl(string cik, string accessionNumber)
    {
        if (string.IsNullOrEmpty(cik) || string.IsNullOrEmpty(accessionNumber))
        {
            return string.Empty;
        }

        var formattedCik = FormatCik(cik);

        // Official SEC pattern for raw text files
        return $"{FilesBaseUrl}/Archives/edgar/data/{formattedCik}/{accessionNumber}.txt";
    }

    private static string FormatCik(string cik)
    {
        return cik.PadLeft(10, '0');
    }

    private static string BuildUrl(string endpoint)
    {
        return $"{BaseUrl}{endpoint}";
    }

    // Per-file URL uses unpadded CIK and the accession number with dashes removed.
    // Padded CIK works too but triggers a 301 redirect; skip the extra hop.
    private static string BuildArchiveUrl(string cik, string accessionNumber, string suffix) =>
        $"{FilesBaseUrl}/Archives/edgar/data/{cik.TrimStart('0')}/{accessionNumber.Replace("-", string.Empty)}/{suffix}";

    private record CachedResponse(string Url, string Content);
}
