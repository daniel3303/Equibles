using System.Globalization;
using System.Net;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Cboe;

public class CboeClient : ICboeClient
{
    private const string PutCallBaseUrl =
        "https://cdn.cboe.com/resources/options/volume_and_call_put_ratios";
    private const string VixUrl =
        "https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv";
    private const int MaxRetries = 3;

    private static readonly Dictionary<CboePutCallCsvType, string> CsvFileNames = new()
    {
        [CboePutCallCsvType.Total] = "totalpc.csv",
        [CboePutCallCsvType.Equity] = "equitypc.csv",
        [CboePutCallCsvType.Index] = "indexpc.csv",
        [CboePutCallCsvType.Vix] = "vixpc.csv",
        [CboePutCallCsvType.Etp] = "etppc.csv",
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CboeClient> _logger;
    private readonly IRateLimiter _rateLimiter;

    public CboeClient(HttpClient httpClient, ILogger<CboeClient> logger, IRateLimiter rateLimiter)
    {
        _httpClient = httpClient;
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public async Task<List<CboePutCallRecord>> DownloadPutCallRatios(CboePutCallCsvType csvType)
    {
        var fileName = CsvFileNames[csvType];
        var url = $"{PutCallBaseUrl}/{fileName}";
        _logger.LogDebug("Downloading CBOE put/call ratios from {Url}", url);

        var content = await DownloadWithRetry(url);
        return ParsePutCallCsv(content);
    }

    public async Task<List<CboeVixRecord>> DownloadVixHistory()
    {
        _logger.LogDebug("Downloading CBOE VIX history from {Url}", VixUrl);

        var content = await DownloadWithRetry(VixUrl);
        return ParseVixCsv(content);
    }

    private async Task<string> DownloadWithRetry(string url)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await _rateLimiter.WaitAsync();

            using var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var delay = ExponentialBackoff(attempt);
                _logger.LogWarning(
                    "CBOE rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );
                _rateLimiter.PauseFor(delay);
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
            {
                var delay = ExponentialBackoff(attempt);
                _logger.LogWarning(
                    "CBOE server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        throw new HttpRequestException("Max retries exceeded for CBOE download");
    }

    private static TimeSpan ExponentialBackoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));

    private static List<CboePutCallRecord> ParsePutCallCsv(string content)
    {
        var records = new List<CboePutCallRecord>();
        foreach (var fields in EnumerateCsvRows(content, minFields: 5))
        {
            if (
                !DateOnly.TryParseExact(
                    fields[0].Trim(),
                    "MM/dd/yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
            )
                continue;

            records.Add(
                new CboePutCallRecord
                {
                    Date = date,
                    CallVolume = ParseLong(fields[1]),
                    PutVolume = ParseLong(fields[2]),
                    TotalVolume = ParseLong(fields[3]),
                    PutCallRatio = ParseDecimal(fields[4]),
                }
            );
        }
        return records;
    }

    private static List<CboeVixRecord> ParseVixCsv(string content)
    {
        var records = new List<CboeVixRecord>();
        foreach (var fields in EnumerateCsvRows(content, minFields: 5))
        {
            bool TryDec(int index, out decimal value) =>
                decimal.TryParse(fields[index].Trim(), CultureInfo.InvariantCulture, out value);

            if (
                !DateOnly.TryParseExact(
                    fields[0].Trim(),
                    "MM/dd/yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
            )
                continue;

            if (!TryDec(1, out var open))
                continue;
            if (!TryDec(2, out var high))
                continue;
            if (!TryDec(3, out var low))
                continue;
            if (!TryDec(4, out var close))
                continue;

            records.Add(
                new CboeVixRecord
                {
                    Date = date,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                }
            );
        }
        return records;
    }

    // First line is the header; subsequent blank or short rows are skipped.
    private static IEnumerable<string[]> EnumerateCsvRows(string content, int minFields)
    {
        using var reader = new StringReader(content);
        reader.ReadLine();

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = line.Split(',');
            if (fields.Length < minFields)
                continue;

            yield return fields;
        }
    }

    private static long? ParseLong(string value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        return long.TryParse(value.Replace(",", ""), CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static decimal? ParseDecimal(string value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        return decimal.TryParse(value, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
