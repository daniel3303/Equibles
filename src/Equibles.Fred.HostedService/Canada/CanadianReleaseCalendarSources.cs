using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Equibles.Fred.Data.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.Fred.HostedService.Canada;

public sealed record CanadianReleaseDate(string Name, DateOnly Date, string ReferencePeriod = null);

// The release dates Statistics Canada and the Bank of Canada publish. Each read must look like the
// schedule it was asked for, or stored dates would be replaced from a page that changed shape.
public static partial class CanadianReleaseCalendarSources
{
    public const string StatCanSchedule =
        "https://www150.statcan.gc.ca/n1/dai-quo/ssi/homepage/schedule-key_indicators-eng.json";
    public const string StatCanPage = "https://www150.statcan.gc.ca/n1/dai-quo/cal1-eng.htm";
    public const string BankOfCanadaPage =
        "https://www.bankofcanada.ca/core-functions/monetary-policy/key-interest-rate/";

    public const string InterestRateAnnouncement = "Interest rate announcement";
    public const string MonetaryPolicyReport = "Monetary Policy Report";

    private const string ConsumerPriceIndex = "Consumer Price Index";
    private const string LabourForceSurvey = "Labour Force Survey";
    private const string GdpByIndustry = "Gross domestic product by industry";
    private const string GdpIncomeExpenditure = "Gross domestic product, income and expenditure";

    // Eight decisions a year; fewer means the table lost rows rather than the Bank lost meetings.
    private const int MinimumDecisionsPerYear = 6;

    // A release this map does not name stays Low, as an unmapped FRED release does.
    private static readonly IReadOnlyDictionary<string, FredReleaseImportance> Tiers =
        new Dictionary<string, FredReleaseImportance>
        {
            [ConsumerPriceIndex] = FredReleaseImportance.High,
            [LabourForceSurvey] = FredReleaseImportance.High,
            [GdpByIndustry] = FredReleaseImportance.High,
            [GdpIncomeExpenditure] = FredReleaseImportance.High,
            [InterestRateAnnouncement] = FredReleaseImportance.High,
            [MonetaryPolicyReport] = FredReleaseImportance.Medium,
            ["Retail trade"] = FredReleaseImportance.Medium,
            ["Canadian international merchandise trade"] = FredReleaseImportance.Medium,
            ["Monthly Survey of Manufacturing"] = FredReleaseImportance.Medium,
            ["Wholesale trade"] = FredReleaseImportance.Medium,
            ["Building permits"] = FredReleaseImportance.Medium,
        };

    // The curated series each scheduled release updates. CORRA and the bond yields are daily levels,
    // published on no schedule, so they belong to no release.
    public static readonly IReadOnlyDictionary<string, string> ReleaseOfSeries = new Dictionary<
        string,
        string
    >
    {
        ["STATCAN-V41690973"] = ConsumerPriceIndex,
        ["STATCAN-V41690914"] = ConsumerPriceIndex,
        ["STATCAN-V108785715"] = ConsumerPriceIndex,
        ["STATCAN-V108785714"] = ConsumerPriceIndex,
        ["STATCAN-V2062815"] = LabourForceSurvey,
        ["STATCAN-V2062811"] = LabourForceSurvey,
        ["STATCAN-V65201210"] = GdpByIndustry,
        ["STATCAN-V62305752"] = GdpIncomeExpenditure,
        ["BOC-V39079"] = InterestRateAnnouncement,
    };

    public static FredReleaseImportance Importance(string name) =>
        Tiers.GetValueOrDefault(name, FredReleaseImportance.Low);

    // Every row back to 2012 is served, so only dates on or after `from` are kept.
    public static IReadOnlyList<CanadianReleaseDate> ParseStatCan(string json, DateOnly from)
    {
        JToken root;
        try
        {
            root = JToken.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Statistics Canada's schedule is not JSON.", exception);
        }
        if (root is not JArray rows)
            throw new InvalidDataException("Statistics Canada's schedule is not a list.");
        var dates = new List<CanadianReleaseDate>();
        foreach (var row in rows)
        {
            var stamp = (string)row["date"];
            var name = ((string)row["title"])?.Trim();
            if (
                string.IsNullOrEmpty(name)
                || stamp is not { Length: >= 10 }
                || !DateOnly.TryParseExact(
                    stamp[..10],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
            )
                throw new InvalidDataException(
                    "Statistics Canada's schedule states a row without its date or title."
                );
            if (date < from)
                continue;
            var period = ((string)row["description"])?.Trim();
            dates.Add(new(name, date, string.IsNullOrEmpty(period) ? null : period));
        }
        return dates.Count > 0
            ? dates.DistinctBy(entry => (entry.Name, entry.Date)).ToList()
            : throw new InvalidDataException(
                $"Statistics Canada's schedule states no release on or after {from:yyyy-MM-dd}."
            );
    }

    // The key-rate page tables one schedule per year: a date, then the publications of that day. The import replaces
    // stored dates with this reading, so a caption or row of another shape refuses the page instead of dropping out.
    public static IReadOnlyList<CanadianReleaseDate> ParseBankOfCanada(string html, DateOnly from)
    {
        var dates = new List<CanadianReleaseDate>();
        var tables = ScheduleTable().Matches(html);
        if (tables.Count != TableCaption().Count(html))
            throw new InvalidDataException(
                "The Bank of Canada's page captions a table it does not state as a year's schedule."
            );
        foreach (Match table in tables)
        {
            var year = table.Groups["year"].Value;
            var body = table.Groups["rows"].Value;
            var rows = ScheduleRow().Matches(body);
            if (rows.Count < MinimumDecisionsPerYear || rows.Count != TableRow().Count(body))
                throw new InvalidDataException(
                    $"The Bank of Canada's {year} schedule states {TableRow().Count(body)} rows, {rows.Count} of them a date and its publications."
                );
            foreach (Match row in rows)
            {
                if (
                    !DateOnly.TryParseExact(
                        $"{Spaced(row.Groups["date"].Value)} {year}",
                        "MMMM d yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date
                    )
                )
                    throw new InvalidDataException(
                        "The Bank of Canada's schedule states a date it does not spell as a month and day."
                    );
                var publications = row.Groups["publications"].Value;
                if (
                    !publications.Contains(
                        InterestRateAnnouncement,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    throw new InvalidDataException(
                        "The Bank of Canada's schedule states a date without an interest rate announcement."
                    );
                if (date < from)
                    continue;
                dates.Add(new(InterestRateAnnouncement, date));
                if (publications.Contains(MonetaryPolicyReport, StringComparison.OrdinalIgnoreCase))
                    dates.Add(new(MonetaryPolicyReport, date));
            }
        }
        return tables.Count > 0 && dates.Count > 0
            ? dates
            : throw new InvalidDataException(
                $"The Bank of Canada's page states no decision on or after {from:yyyy-MM-dd}."
            );
    }

    // The page writes a date's space as a no-break space, raw in one year's table and an entity in the next.
    private static string Spaced(string cell) =>
        WebUtility.HtmlDecode(cell).Replace('\u00A0', ' ').Trim();

    [GeneratedRegex(
        @"<caption[^>]*>\s*Schedule for (?<year>\d{4})\s*</caption>(?:(?!</table>).)*?<tbody[^>]*>(?<rows>.*?)</tbody>",
        RegexOptions.Singleline
    )]
    private static partial Regex ScheduleTable();

    [GeneratedRegex(@"<caption\b")]
    private static partial Regex TableCaption();

    [GeneratedRegex(@"<tr\b")]
    private static partial Regex TableRow();

    [GeneratedRegex(
        @"<tr>\s*<td>(?<date>[^<]+)</td>\s*<td>(?<publications>.*?)</td>\s*</tr>",
        RegexOptions.Singleline
    )]
    private static partial Regex ScheduleRow();
}
