using System.Globalization;
using System.IO.Compression;
using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Cftc.Contracts;
using Equibles.Integrations.Cftc.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Cftc;

[Service(ServiceLifetime.Scoped, typeof(ICftcClient))]
public class CftcClient : ICftcClient {
    private const string BaseUrl = "https://www.cftc.gov/files/dea/history";
    private const int MaxRetries = 3;

    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 5, timeWindow: TimeSpan.FromMinutes(1));

    private readonly HttpClient _httpClient;
    private readonly ILogger<CftcClient> _logger;

    public CftcClient(HttpClient httpClient, ILogger<CftcClient> logger) {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<CftcReportRecord>> DownloadYearlyReport(int year) {
        var url = $"{BaseUrl}/deacot{year}.zip";
        _logger.LogDebug("Downloading CFTC COT report for year {Year} from {Url}", year, url);

        var zipStream = await DownloadWithRetry(url);
        return await ParseZipArchive(zipStream);
    }

    private async Task<Stream> DownloadWithRetry(string url) {
        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            await RateLimiter.WaitAsync();

            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("CFTC rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, MaxRetries);
                RateLimiter.PauseFor(delay);
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("CFTC server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode, delay.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();

            // Copy to memory stream so we can use ZipArchive (requires seekable stream)
            var memoryStream = new MemoryStream();
            await response.Content.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        throw new HttpRequestException("Max retries exceeded for CFTC download");
    }

    private async Task<List<CftcReportRecord>> ParseZipArchive(Stream zipStream) {
        var records = new List<CftcReportRecord>();

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault();
        if (entry == null) return records;

        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);

        // Read header line and build column index
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null) return records;

        var columnIndex = BuildColumnIndex(headerLine);

        // Parse data lines
        while (await reader.ReadLineAsync() is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var record = ParseLine(line, columnIndex);
            if (record != null) {
                records.Add(record);
            }
        }

        _logger.LogDebug("Parsed {Count} CFTC COT records from ZIP archive", records.Count);
        return records;
    }

    private static Dictionary<string, int> BuildColumnIndex(string headerLine) {
        var headers = headerLine.Split(',');
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < headers.Length; i++) {
            var header = headers[i].Trim().Trim('"');
            index[header] = i;
        }

        return index;
    }

    private static CftcReportRecord ParseLine(string line, Dictionary<string, int> columnIndex) {
        var fields = SplitCsvLine(line);

        var record = new CftcReportRecord {
            MarketAndExchangeName = GetField(fields, columnIndex, "Market_and_Exchange_Names"),
            ReportDate = GetField(fields, columnIndex, "Report_Date_as_YYYY-MM-DD")
                         ?? GetField(fields, columnIndex, "As_of_Date_In_Form_YYMMDD"),
            ContractMarketCode = GetField(fields, columnIndex, "CFTC_Contract_Market_Code"),
            OpenInterest = ParseLong(GetField(fields, columnIndex, "Open_Interest_All")),
            NonCommLong = ParseLong(GetField(fields, columnIndex, "NonComm_Positions_Long_All")),
            NonCommShort = ParseLong(GetField(fields, columnIndex, "NonComm_Positions_Short_All")),
            NonCommSpreads = ParseLong(GetField(fields, columnIndex, "NonComm_Positions_Spread_All")),
            CommLong = ParseLong(GetField(fields, columnIndex, "Comm_Positions_Long_All")),
            CommShort = ParseLong(GetField(fields, columnIndex, "Comm_Positions_Short_All")),
            TotalRptLong = ParseLong(GetField(fields, columnIndex, "Tot_Rpt_Positions_Long_All")),
            TotalRptShort = ParseLong(GetField(fields, columnIndex, "Tot_Rpt_Positions_Short_All")),
            NonRptLong = ParseLong(GetField(fields, columnIndex, "NonRpt_Positions_Long_All")),
            NonRptShort = ParseLong(GetField(fields, columnIndex, "NonRpt_Positions_Short_All")),
            ChangeOpenInterest = ParseLong(GetField(fields, columnIndex, "Change_in_Open_Interest_All")),
            ChangeNonCommLong = ParseLong(GetField(fields, columnIndex, "Change_in_NonComm_Long_All")),
            ChangeNonCommShort = ParseLong(GetField(fields, columnIndex, "Change_in_NonComm_Short_All")),
            ChangeCommLong = ParseLong(GetField(fields, columnIndex, "Change_in_Comm_Long_All")),
            ChangeCommShort = ParseLong(GetField(fields, columnIndex, "Change_in_Comm_Short_All")),
            PctNonCommLong = ParseDecimal(GetField(fields, columnIndex, "Pct_of_OI_NonComm_Long_All")),
            PctNonCommShort = ParseDecimal(GetField(fields, columnIndex, "Pct_of_OI_NonComm_Short_All")),
            PctCommLong = ParseDecimal(GetField(fields, columnIndex, "Pct_of_OI_Comm_Long_All")),
            PctCommShort = ParseDecimal(GetField(fields, columnIndex, "Pct_of_OI_Comm_Short_All")),
            TradersTotal = ParseInt(GetField(fields, columnIndex, "Traders_Tot_All")),
            TradersNonCommLong = ParseInt(GetField(fields, columnIndex, "Traders_NonComm_Long_All")),
            TradersNonCommShort = ParseInt(GetField(fields, columnIndex, "Traders_NonComm_Short_All")),
            TradersCommLong = ParseInt(GetField(fields, columnIndex, "Traders_Comm_Long_All")),
            TradersCommShort = ParseInt(GetField(fields, columnIndex, "Traders_Comm_Short_All"))
        };

        return record;
    }

    private static string[] SplitCsvLine(string line) {
        // CFTC files use comma-separated values with quoted fields
        var fields = new List<string>();
        var inQuotes = false;
        var start = 0;

        for (var i = 0; i < line.Length; i++) {
            if (line[i] == '"') {
                inQuotes = !inQuotes;
            } else if (line[i] == ',' && !inQuotes) {
                fields.Add(line[start..i].Trim().Trim('"'));
                start = i + 1;
            }
        }

        fields.Add(line[start..].Trim().Trim('"'));
        return fields.ToArray();
    }

    private static string GetField(string[] fields, Dictionary<string, int> columnIndex, string columnName) {
        if (!columnIndex.TryGetValue(columnName, out var idx) || idx >= fields.Length) return null;
        var value = fields[idx].Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static long? ParseLong(string value) {
        if (value == null) return null;
        return long.TryParse(value.Replace(",", ""), CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static decimal? ParseDecimal(string value) {
        if (value == null) return null;
        return decimal.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static int? ParseInt(string value) {
        if (value == null) return null;
        return int.TryParse(value.Replace(",", ""), CultureInfo.InvariantCulture, out var result) ? result : null;
    }
}
