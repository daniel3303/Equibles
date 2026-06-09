using System.Globalization;
using System.Text;
using System.Text.Json;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Cboe;

public class CboeClient : ICboeClient
{
    // CBOE retired the historical put/call CSV feed in October 2019 (the CDN files
    // freeze at 2019-10-04). The daily market-statistics page is the only freely
    // available source going forward — server-rendered HTML with the day's data
    // embedded as JSON in the React Server Component payload.
    private const string DailyStatsUrl =
        "https://www.cboe.com/markets/us/options/market-statistics/daily/";
    private const string VixUrl =
        "https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv";
    private const int MaxRetries = 3;

    private static readonly (
        CboePutCallProductType Product,
        string RatioKey,
        string VolumeKey
    )[] ProductMappings =
    [
        (CboePutCallProductType.Total, "TOTAL PUT/CALL RATIO", "SUM OF ALL PRODUCTS"),
        (CboePutCallProductType.Equity, "EQUITY PUT/CALL RATIO", "EQUITY OPTIONS"),
        (CboePutCallProductType.Index, "INDEX PUT/CALL RATIO", "INDEX OPTIONS"),
        (
            CboePutCallProductType.Vix,
            "CBOE VOLATILITY INDEX (VIX) PUT/CALL RATIO",
            "CBOE VOLATILITY INDEX (VIX)"
        ),
        (
            CboePutCallProductType.Etp,
            "EXCHANGE TRADED PRODUCTS PUT/CALL RATIO",
            "EXCHANGE TRADED PRODUCTS"
        ),
    ];

    private readonly HttpClient _httpClient;
    private readonly ILogger<CboeClient> _logger;
    private readonly IRateLimiter _rateLimiter;

    public CboeClient(HttpClient httpClient, ILogger<CboeClient> logger, IRateLimiter rateLimiter)
    {
        _httpClient = httpClient;
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public async Task<
        Dictionary<CboePutCallProductType, CboePutCallRecord>
    > DownloadDailyPutCallRatios(DateOnly date)
    {
        var url = $"{DailyStatsUrl}?dt={date:yyyy-MM-dd}";
        _logger.LogDebug("Downloading CBOE daily put/call ratios for {Date}", date);

        var html = await DownloadWithRetry(url);
        return ParseDailyPutCallPage(html, date);
    }

    public async Task<List<CboeVixRecord>> DownloadVixHistory()
    {
        _logger.LogDebug("Downloading CBOE VIX history from {Url}", VixUrl);

        var content = await DownloadWithRetry(VixUrl);
        return ParseVixCsv(content);
    }

    private async Task<string> DownloadWithRetry(string url)
    {
        using var response = await HttpRetry.Send(
            () => _httpClient.GetAsync(url),
            _rateLimiter,
            MaxRetries,
            "Max retries exceeded for CBOE download",
            (attempt, delay) =>
                _logger.LogWarning(
                    "CBOE rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                ),
            (statusCode, attempt, delay) =>
                _logger.LogWarning(
                    "CBOE server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    statusCode,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                )
        );
        return await response.Content.ReadAsStringAsync();
    }

    // Thin forwarder so existing reflection-based backoff tests still find the method.
    private static TimeSpan ExponentialBackoff(int attempt) => RetryBackoff.Exponential(attempt);

    private static Dictionary<CboePutCallProductType, CboePutCallRecord> ParseDailyPutCallPage(
        string html,
        DateOnly date
    )
    {
        // Non-trading days (weekends, holidays, future dates) render the page
        // skeleton without the optionsData block — return an empty dictionary
        // so the import service can no-op for that date.
        var optionsJson = ExtractOptionsDataJson(html);
        if (optionsJson is null)
            return new();

        using var doc = JsonDocument.Parse(optionsJson);
        var root = doc.RootElement;
        var ratios = ParseRatios(root);

        var result = new Dictionary<CboePutCallProductType, CboePutCallRecord>();
        foreach (var (product, ratioKey, volumeKey) in ProductMappings)
        {
            var volume = TryGetVolume(root, volumeKey);
            ratios.TryGetValue(ratioKey, out var ratio);
            if (volume is null && ratio is null)
                continue;

            result[product] = new CboePutCallRecord
            {
                Date = date,
                CallVolume = volume?.Call,
                PutVolume = volume?.Put,
                TotalVolume = volume?.Total,
                PutCallRatio = ratio,
            };
        }
        return result;
    }

    // The daily-stats page is rendered by Next.js App Router; the day's data
    // sits inside the React Server Component payload as a JSON string, so each
    // `"` is escaped to `\"`. We locate the `"optionsData\":` marker, walk
    // balanced braces (escape-aware), then JSON-unescape the captured slice
    // so it can be parsed as normal JSON.
    private const string RscChunkBoundary = "\"])</script><script>self.__next_f.push([1,\"";

    private static string ExtractOptionsDataJson(string html)
    {
        // Next.js streams the RSC payload as consecutive self.__next_f.push([1,"…"])
        // script chunks, and a chunk boundary can fall anywhere — including inside
        // the optionsData JSON (it does since 2026-06-08, when the page grew new
        // products). Stitch the chunks back together before extracting, otherwise
        // the brace walk drags the literal boundary markup into the slice.
        html = html.Replace(RscChunkBoundary, "", StringComparison.Ordinal);

        const string marker = "\"optionsData\\\":";
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return null;
        var start = markerIndex + marker.Length;
        if (start >= html.Length || html[start] != '{')
            return null;

        var depth = 0;
        var inString = false;
        var end = -1;
        for (var i = start; i < html.Length; i++)
        {
            var c = html[i];
            if (c == '\\' && i + 1 < html.Length)
            {
                // String delimiters in the double-escaped RSC payload appear as \" .
                // Toggle string context on \" ; skip any other escape (\\, \n, …)
                // whole so its payload can't be misread as a structural brace.
                if (html[i + 1] == '"')
                    inString = !inString;
                i++;
                continue;
            }
            if (inString)
                continue;
            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = i + 1;
                    break;
                }
            }
        }
        if (end < 0)
            return null;

        return JsonStringUnescape(html.AsSpan(start, end - start));
    }

    private static string JsonStringUnescape(ReadOnlySpan<char> input)
    {
        var sb = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] != '\\' || i + 1 >= input.Length)
            {
                sb.Append(input[i]);
                continue;
            }
            var next = input[++i];
            switch (next)
            {
                case '"':
                case '\\':
                case '/':
                    sb.Append(next);
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'u' when i + 4 < input.Length:
                    var hex = input.Slice(i + 1, 4);
                    if (
                        ushort.TryParse(
                            hex,
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out var code
                        )
                    )
                    {
                        sb.Append((char)code);
                        i += 4;
                    }
                    else
                    {
                        sb.Append('\\').Append(next);
                    }
                    break;
                default:
                    sb.Append('\\').Append(next);
                    break;
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, decimal?> ParseRatios(JsonElement root)
    {
        var result = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        if (
            !root.TryGetProperty("ratios", out var ratios)
            || ratios.ValueKind != JsonValueKind.Array
        )
            return result;

        foreach (var entry in ratios.EnumerateArray())
        {
            if (
                !entry.TryGetProperty("name", out var nameElement)
                || !entry.TryGetProperty("value", out var valueElement)
            )
                continue;
            var name = nameElement.GetString();
            if (string.IsNullOrEmpty(name))
                continue;
            result[name] = ParseDecimal(valueElement.GetString());
        }
        return result;
    }

    private static (long? Call, long? Put, long? Total)? TryGetVolume(
        JsonElement root,
        string categoryKey
    )
    {
        if (
            !root.TryGetProperty(categoryKey, out var category)
            || category.ValueKind != JsonValueKind.Array
        )
            return null;

        foreach (var entry in category.EnumerateArray())
        {
            if (
                !entry.TryGetProperty("name", out var nameElement)
                || nameElement.GetString() != "VOLUME"
            )
                continue;
            entry.TryGetProperty("call", out var callEl);
            entry.TryGetProperty("put", out var putEl);
            entry.TryGetProperty("total", out var totalEl);
            return (TryGetLong(callEl), TryGetLong(putEl), TryGetLong(totalEl));
        }
        return null;
    }

    private static long? TryGetLong(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var v) ? v : null;

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
