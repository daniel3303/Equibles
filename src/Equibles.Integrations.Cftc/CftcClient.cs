using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Cftc.Contracts;
using Equibles.Integrations.Cftc.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Cftc;

[Service(ServiceLifetime.Scoped, typeof(ICftcClient))]
public class CftcClient : ICftcClient
{
    private const string BaseUrl = "https://www.cftc.gov/files/dea/history";
    private const int MaxRetries = 3;

    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromMinutes(1)
    );

    private readonly HttpClient _httpClient;
    private readonly ILogger<CftcClient> _logger;

    public CftcClient(HttpClient httpClient, ILogger<CftcClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<CftcReportRecord>> DownloadYearlyReport(int year)
    {
        var url = $"{BaseUrl}/deacot{year}.zip";
        _logger.LogDebug("Downloading CFTC COT report for year {Year} from {Url}", year, url);

        var zipStream = await DownloadWithRetry(url);
        return await ParseZipArchive(zipStream);
    }

    private async Task<Stream> DownloadWithRetry(string url)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync();

            var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead
            );

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning(
                    "CFTC rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );
                RateLimiter.PauseFor(delay);
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning(
                    "CFTC server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );
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

    private async Task<List<CftcReportRecord>> ParseZipArchive(Stream zipStream)
    {
        var records = new List<CftcReportRecord>();

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault();
        if (entry == null)
            return records;

        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);

        // Read header line and build column index
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null)
            return records;

        var columnIndex = BuildColumnIndex(headerLine);

        // Parse data lines
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var record = ParseLine(line, columnIndex);
            if (record != null)
            {
                records.Add(record);
            }
        }

        _logger.LogDebug("Parsed {Count} CFTC COT records from ZIP archive", records.Count);
        return records;
    }

    private static Dictionary<string, int> BuildColumnIndex(string headerLine)
    {
        var headers = headerLine.Split(',');
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < headers.Length; i++)
        {
            var header = headers[i].Trim().Trim('"');
            index[header] = i;
        }

        return index;
    }

    private static CftcReportRecord ParseLine(string line, Dictionary<string, int> columnIndex)
    {
        var fields = SplitCsvLine(line);
        string Get(string column) => GetField(fields, columnIndex, column);

        return new CftcReportRecord
        {
            MarketAndExchangeName = Get("Market_and_Exchange_Names"),
            ReportDate = Get("Report_Date_as_YYYY-MM-DD") ?? Get("As_of_Date_In_Form_YYMMDD"),
            ContractMarketCode = Get("CFTC_Contract_Market_Code"),
            OpenInterest = ParseLong(Get("Open_Interest_All")),
            NonCommLong = ParseLong(Get("NonComm_Positions_Long_All")),
            NonCommShort = ParseLong(Get("NonComm_Positions_Short_All")),
            NonCommSpreads = ParseLong(Get("NonComm_Positions_Spread_All")),
            CommLong = ParseLong(Get("Comm_Positions_Long_All")),
            CommShort = ParseLong(Get("Comm_Positions_Short_All")),
            TotalRptLong = ParseLong(Get("Tot_Rpt_Positions_Long_All")),
            TotalRptShort = ParseLong(Get("Tot_Rpt_Positions_Short_All")),
            NonRptLong = ParseLong(Get("NonRpt_Positions_Long_All")),
            NonRptShort = ParseLong(Get("NonRpt_Positions_Short_All")),
            ChangeOpenInterest = ParseLong(Get("Change_in_Open_Interest_All")),
            ChangeNonCommLong = ParseLong(Get("Change_in_NonComm_Long_All")),
            ChangeNonCommShort = ParseLong(Get("Change_in_NonComm_Short_All")),
            ChangeCommLong = ParseLong(Get("Change_in_Comm_Long_All")),
            ChangeCommShort = ParseLong(Get("Change_in_Comm_Short_All")),
            PctNonCommLong = ParseDecimal(Get("Pct_of_OI_NonComm_Long_All")),
            PctNonCommShort = ParseDecimal(Get("Pct_of_OI_NonComm_Short_All")),
            PctCommLong = ParseDecimal(Get("Pct_of_OI_Comm_Long_All")),
            PctCommShort = ParseDecimal(Get("Pct_of_OI_Comm_Short_All")),
            TradersTotal = ParseInt(Get("Traders_Tot_All")),
            TradersNonCommLong = ParseInt(Get("Traders_NonComm_Long_All")),
            TradersNonCommShort = ParseInt(Get("Traders_NonComm_Short_All")),
            TradersCommLong = ParseInt(Get("Traders_Comm_Long_All")),
            TradersCommShort = ParseInt(Get("Traders_Comm_Short_All")),
        };
    }

    private static string[] SplitCsvLine(string line)
    {
        // CFTC files use comma-separated values with quoted fields. Follows
        // RFC 4180: inside a quoted field a doubled "" is one literal ".
        // Quoted content is taken verbatim; unquoted fields are whitespace-trimmed.
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var wasQuoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
                wasQuoted = true;
                // CFTC pads delimiters as `, "value"`. The space(s) before the
                // opening quote are not part of the quoted content — discard
                // them so the value is taken verbatim from inside the quotes.
                field.Clear();
            }
            else if (c == ',')
            {
                fields.Add(wasQuoted ? field.ToString() : field.ToString().Trim());
                field.Clear();
                wasQuoted = false;
            }
            else if (!wasQuoted)
            {
                field.Append(c);
            }
            // Characters after a closing quote (CFTC's trailing padding before
            // the next delimiter) are likewise not part of the verbatim value.
        }

        fields.Add(wasQuoted ? field.ToString() : field.ToString().Trim());
        return fields.ToArray();
    }

    private static string GetField(
        string[] fields,
        Dictionary<string, int> columnIndex,
        string columnName
    )
    {
        if (!columnIndex.TryGetValue(columnName, out var idx) || idx >= fields.Length)
            return null;
        var value = fields[idx].Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static long? ParseLong(string value)
    {
        if (value == null)
            return null;
        return long.TryParse(value.Replace(",", ""), CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static decimal? ParseDecimal(string value)
    {
        if (value == null)
            return null;
        return decimal.TryParse(value, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static int? ParseInt(string value)
    {
        if (value == null)
            return null;
        return int.TryParse(value.Replace(",", ""), CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
