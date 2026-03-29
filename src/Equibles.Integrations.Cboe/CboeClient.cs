using System.Globalization;
using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Cboe;

[Service(ServiceLifetime.Scoped, typeof(ICboeClient))]
public class CboeClient : ICboeClient {
    private const string PutCallBaseUrl = "https://cdn.cboe.com/resources/options/volume_and_call_put_ratios";
    private const string VixUrl = "https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv";
    private const int MaxRetries = 3;

    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 10, timeWindow: TimeSpan.FromMinutes(1));

    private static readonly Dictionary<CboePutCallCsvType, string> CsvFileNames = new() {
        [CboePutCallCsvType.Total] = "totalpc.csv",
        [CboePutCallCsvType.Equity] = "equitypc.csv",
        [CboePutCallCsvType.Index] = "indexpc.csv",
        [CboePutCallCsvType.Vix] = "vixpc.csv",
        [CboePutCallCsvType.Etp] = "etppc.csv"
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CboeClient> _logger;

    public CboeClient(HttpClient httpClient, ILogger<CboeClient> logger) {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<CboePutCallRecord>> DownloadPutCallRatios(CboePutCallCsvType csvType) {
        var fileName = CsvFileNames[csvType];
        var url = $"{PutCallBaseUrl}/{fileName}";
        _logger.LogDebug("Downloading CBOE put/call ratios from {Url}", url);

        var content = await DownloadWithRetry(url);
        return ParsePutCallCsv(content);
    }

    public async Task<List<CboeVixRecord>> DownloadVixHistory() {
        _logger.LogDebug("Downloading CBOE VIX history from {Url}", VixUrl);

        var content = await DownloadWithRetry(VixUrl);
        return ParseVixCsv(content);
    }

    private async Task<string> DownloadWithRetry(string url) {
        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            await RateLimiter.WaitAsync();

            using var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("CBOE rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, MaxRetries);
                RateLimiter.PauseFor(delay);
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("CBOE server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode, delay.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        throw new HttpRequestException("Max retries exceeded for CBOE download");
    }

    private static List<CboePutCallRecord> ParsePutCallCsv(string content) {
        var records = new List<CboePutCallRecord>();
        using var reader = new StringReader(content);

        // Skip header line
        reader.ReadLine();

        while (reader.ReadLine() is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = line.Split(',');
            if (fields.Length < 5) continue;

            if (!DateOnly.TryParseExact(fields[0].Trim(), "MM/dd/yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;

            records.Add(new CboePutCallRecord {
                Date = date,
                CallVolume = ParseLong(fields[1]),
                PutVolume = ParseLong(fields[2]),
                TotalVolume = ParseLong(fields[3]),
                PutCallRatio = ParseDecimal(fields[4])
            });
        }

        return records;
    }

    private static List<CboeVixRecord> ParseVixCsv(string content) {
        var records = new List<CboeVixRecord>();
        using var reader = new StringReader(content);

        // Skip header line
        reader.ReadLine();

        while (reader.ReadLine() is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = line.Split(',');
            if (fields.Length < 5) continue;

            if (!DateOnly.TryParseExact(fields[0].Trim(), "MM/dd/yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;

            if (!decimal.TryParse(fields[1].Trim(), CultureInfo.InvariantCulture, out var open)) continue;
            if (!decimal.TryParse(fields[2].Trim(), CultureInfo.InvariantCulture, out var high)) continue;
            if (!decimal.TryParse(fields[3].Trim(), CultureInfo.InvariantCulture, out var low)) continue;
            if (!decimal.TryParse(fields[4].Trim(), CultureInfo.InvariantCulture, out var close)) continue;

            records.Add(new CboeVixRecord {
                Date = date,
                Open = open,
                High = high,
                Low = low,
                Close = close
            });
        }

        return records;
    }

    private static long? ParseLong(string value) {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return long.TryParse(value.Replace(",", ""), CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static decimal? ParseDecimal(string value) {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return decimal.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : null;
    }
}
