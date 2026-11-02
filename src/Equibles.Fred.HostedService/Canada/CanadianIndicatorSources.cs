using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.Fred.HostedService.Canada;

public sealed record IndicatorPoint(DateOnly Date, decimal? Value);

// The observation formats of the Bank of Canada's Valet API and Statistics Canada's Web Data
// Service. Each answer must be about the series asked for; dates and numbers are read as stated,
// never through the machine's culture.
public static class CanadianIndicatorSources
{
    public static Uri ValetAddress(string key, DateOnly from) =>
        new(
            $"https://www.bankofcanada.ca/valet/observations/{Uri.EscapeDataString(key)}/json?start_date={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        );

    public static Uri StatCanAddress(string vectorId, DateOnly from, DateOnly to) =>
        new(
            "https://www150.statcan.gc.ca/t1/wds/rest/getDataFromVectorByReferencePeriodRange"
                + $"?vectorIds=%22{Uri.EscapeDataString(vectorId)}%22"
                + $"&startRefPeriod={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                + $"&endReferencePeriod={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        );

    // Valet leaves a day out, or states it without a value, when nothing was published for it.
    public static IReadOnlyList<IndicatorPoint> ParseValet(string json, string key)
    {
        var root =
            Parse(json) as JObject ?? throw new InvalidDataException("Valet answered no object.");
        if (root["seriesDetail"]?[key] == null)
            throw new InvalidDataException("Valet answered for another series.");
        if (root["observations"] is not JArray observations)
            throw new InvalidDataException("Valet answered no observations.");
        var points = new List<IndicatorPoint>();
        foreach (var row in observations)
        {
            var date = Date((string)row["d"], "Valet");
            var value = (string)row[key]?["v"];
            if (string.IsNullOrWhiteSpace(value))
                continue;
            points.Add(
                new(date, decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture))
            );
        }
        return points;
    }

    // A period Statistics Canada suppresses is stated without a value and kept as a gap.
    public static IReadOnlyList<IndicatorPoint> ParseStatCan(string json, string vectorId)
    {
        if (Parse(json) is not JArray answers)
            throw new InvalidDataException("Statistics Canada answered no list.");
        var answer =
            answers.SingleOrDefault(a =>
                (string)a["status"] == "SUCCESS"
                && (string)a["object"]?["vectorId"]
                    == long.Parse(vectorId, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture)
            ) ?? throw new InvalidDataException("Statistics Canada answered for another vector.");
        if (answer["object"]!["vectorDataPoint"] is not JArray rows)
            throw new InvalidDataException("Statistics Canada answered no data points.");
        return rows.Select(row => new IndicatorPoint(
                Date((string)row["refPer"], "Statistics Canada"),
                row["value"]?.Type is JTokenType.Float or JTokenType.Integer
                    ? row["value"]!.Value<decimal>()
                    : null
            ))
            .ToList();
    }

    private static JToken Parse(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
        };
        return JToken.ReadFrom(reader);
    }

    private static DateOnly Date(string value, string source) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? date
            : throw new InvalidDataException($"{source} stated an observation without its date.");
}
