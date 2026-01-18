using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.Congress.Data.Models;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Congress.HostedService.Models;
using UglyToad.PdfPig;
using static Equibles.Congress.HostedService.Services.DisclosureParsingHelper;

using Equibles.Core.AutoWiring;

namespace Equibles.Congress.HostedService.Services;

[Service]
public partial class HouseDisclosureClient {
    private static readonly IRateLimiter RateLimiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromSeconds(1));
    private const int MaxRetries = 3;

    private readonly HttpClient _httpClient;
    private readonly ILogger<HouseDisclosureClient> _logger;

    private const string BaseUrl = "https://disclosures-clerk.house.gov";
    private const string ZipUrlTemplate = BaseUrl + "/public_disc/financial-pdfs/{0}FD.zip";
    private const string PtrPdfUrlTemplate = BaseUrl + "/public_disc/ptr-pdfs/{0}/{1}.pdf";

    public HouseDisclosureClient(HttpClient httpClient, ILogger<HouseDisclosureClient> logger) {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<DisclosureTransaction>> GetRecentTransactions(DateOnly fromDate, DateOnly toDate, CancellationToken ct) {
        var transactions = new List<DisclosureTransaction>();
        var years = Enumerable.Range(fromDate.Year, toDate.Year - fromDate.Year + 1);

        foreach (var year in years) {
            ct.ThrowIfCancellationRequested();
            try {
                var filings = await DownloadAndParseFilingIndex(year, fromDate, toDate, ct);
                _logger.LogInformation("Found {Count} House PTR filings for year {Year}", filings.Count, year);

                foreach (var filing in filings) {
                    try {
                        ct.ThrowIfCancellationRequested();
                        var txns = await DownloadAndParsePtrPdf(filing, year, ct);
                        transactions.AddRange(txns);
                    } catch (OperationCanceledException) {
                        throw;
                    } catch (Exception ex) {
                        _logger.LogWarning(ex, "Failed to parse House PTR PDF for {Member} (DocID {DocId})",
                            filing.MemberName, filing.DocId);
                    }
                }
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to download House filing index for year {Year}", year);
            }
        }

        _logger.LogInformation("Parsed {Count} transactions from House PTR filings", transactions.Count);
        return transactions;
    }

    private async Task<List<HouseFiling>> DownloadAndParseFilingIndex(int year, DateOnly from, DateOnly to, CancellationToken ct) {
        await RateLimiter.WaitAsync();

        var zipUrl = string.Format(ZipUrlTemplate, year);
        using var response = await _httpClient.GetAsync(zipUrl, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) {
            _logger.LogDebug("House FD ZIP not found for year {Year}", year);
            return [];
        }

        response.EnsureSuccessStatusCode();

        using var zipStream = await response.Content.ReadAsStreamAsync(ct);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var xmlEntry = archive.GetEntry($"{year}FD.xml");
        if (xmlEntry == null) {
            _logger.LogWarning("No XML index found in House FD ZIP for year {Year}", year);
            return [];
        }

        await using var xmlStream = xmlEntry.Open();
        var doc = await XDocument.LoadAsync(xmlStream, LoadOptions.None, ct);

        return doc.Descendants("Member")
            .Where(m => m.Element("FilingType")?.Value == "P")
            .Select(m => {
                var filingDateStr = m.Element("FilingDate")?.Value;
                DateOnly.TryParse(filingDateStr, out var filingDate);
                var prefix = m.Element("Prefix")?.Value?.Trim() ?? "";
                var first = m.Element("First")?.Value?.Trim() ?? "";
                var last = m.Element("Last")?.Value?.Trim() ?? "";
                var name = $"{prefix} {first} {last}".Trim()
                    .Replace("Hon. ", "").Replace("Mr. ", "").Replace("Mrs. ", "").Replace("Ms. ", "").Trim();

                return new HouseFiling(
                    name,
                    m.Element("DocID")?.Value ?? "",
                    filingDate,
                    m.Element("StateDst")?.Value ?? ""
                );
            })
            .Where(f => !string.IsNullOrEmpty(f.DocId)
                         && !string.IsNullOrEmpty(f.MemberName)
                         && f.FilingDate >= from
                         && f.FilingDate <= to)
            .ToList();
    }

    private async Task<List<DisclosureTransaction>> DownloadAndParsePtrPdf(HouseFiling filing, int year, CancellationToken ct) {
        await RateLimiter.WaitAsync();

        var pdfUrl = string.Format(PtrPdfUrlTemplate, year, filing.DocId);
        using var response = await SendWithRetryAsync(pdfUrl, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) {
            _logger.LogDebug("House PTR PDF not found: {Url}", pdfUrl);
            return [];
        }

        response.EnsureSuccessStatusCode();

        var pdfBytes = await response.Content.ReadAsByteArrayAsync(ct);
        return ParsePtrPdf(pdfBytes, filing);
    }

    private List<DisclosureTransaction> ParsePtrPdf(byte[] pdfBytes, HouseFiling filing) {
        var transactions = new List<DisclosureTransaction>();

        try {
            using var document = PdfDocument.Open(pdfBytes);

            foreach (var page in document.GetPages()) {
                var text = page.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                transactions.AddRange(ParseTransactionLines(lines, filing));
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to read House PTR PDF for {Member} (DocID {DocId})",
                filing.MemberName, filing.DocId);
        }

        return transactions;
    }

    private List<DisclosureTransaction> ParseTransactionLines(string[] lines, HouseFiling filing) {
        var transactions = new List<DisclosureTransaction>();

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i];

            // Look for lines with owner codes (SP, JT, DC, Self) followed by asset description
            // Transaction lines typically start with an owner code and contain a date pattern
            var ownerMatch = OwnerCodeRegex().Match(line);
            if (!ownerMatch.Success) continue;

            var owner = ownerMatch.Groups[1].Value;
            var remainder = line[ownerMatch.Length..].Trim();

            // Extract ticker from parentheses in asset description
            var ticker = ExtractTickerFromAssetName(remainder);

            // Look for transaction type and date in same line or next lines
            // House PTR format: Owner Asset Type Date NotificationDate Amount
            var dateMatch = DatePatternRegex().Match(remainder);
            if (!dateMatch.Success) continue;

            var txDateStr = dateMatch.Groups[0].Value;
            var txDate = ParseDate(txDateStr);
            if (txDate == null) continue;

            // Extract asset name (everything before the transaction type/date)
            var assetName = remainder[..dateMatch.Index].Trim();

            // Transaction type appears right before the date: "P 01/14/2025" or "S (partial) 12/31/2024"
            var txType = ExtractTransactionType(assetName);
            if (txType == null) continue;

            // Remove the transaction type from the end of the asset name
            assetName = RemoveTrailingTransactionType(assetName).Trim();
            if (string.IsNullOrEmpty(assetName) && string.IsNullOrEmpty(ticker)) continue;

            // Extract amount — look in the remainder after the date(s)
            var afterDates = remainder[dateMatch.Index..];
            var (amountFrom, amountTo) = ParseAmountRange(afterDates);

            transactions.Add(new DisclosureTransaction {
                MemberName = filing.MemberName,
                Position = CongressPosition.Representative,
                Ticker = ticker?.ToUpperInvariant(),
                AssetName = Truncate(assetName, 256),
                TransactionDate = txDate.Value,
                FilingDate = filing.FilingDate,
                TransactionType = txType.Value,
                OwnerType = owner,
                AmountFrom = amountFrom,
                AmountTo = amountTo
            });
        }

        return transactions;
    }

    private static CongressTransactionType? ExtractTransactionType(string text) {
        // House uses: P (Purchase), S (Sale), S (partial), S (full)
        if (SaleTypeRegex().IsMatch(text)) return CongressTransactionType.Sale;
        if (PurchaseTypeRegex().IsMatch(text)) return CongressTransactionType.Purchase;
        return null;
    }

    private static string RemoveTrailingTransactionType(string text) {
        text = SaleTypeRegex().Replace(text, "");
        text = PurchaseTypeRegex().Replace(text, "");
        return text.TrimEnd();
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken ct) {
        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            await RateLimiter.WaitAsync();
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("House disclosure rate limited (429), retrying in {Delay}s", delay.TotalSeconds);
                RateLimiter.PauseFor(delay);
                response.Dispose();
                await Task.Delay(delay, ct);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("House disclosure server error ({StatusCode}), retrying in {Delay}s",
                    (int)response.StatusCode, delay.TotalSeconds);
                response.Dispose();
                await Task.Delay(delay, ct);
                continue;
            }

            return response;
        }

        throw new HttpRequestException($"Max retries ({MaxRetries}) exceeded for House disclosure request: {url}");
    }

    // Owner codes: SP (Spouse), JT (Joint), DC (Dependent Child), or at line start
    [GeneratedRegex(@"^(SP|JT|DC|Self)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OwnerCodeRegex();

    // Date pattern: MM/DD/YYYY
    [GeneratedRegex(@"\b(\d{2}/\d{2}/\d{4})\b")]
    private static partial Regex DatePatternRegex();

    // House sale types at end of text (before date)
    [GeneratedRegex(@"\bS\s*(\((?:partial|full)\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SaleTypeRegex();

    // House purchase type at end of text (before date)
    [GeneratedRegex(@"\bP\s*$")]
    private static partial Regex PurchaseTypeRegex();

    private record HouseFiling(string MemberName, string DocId, DateOnly FilingDate, string StateDst);
}
