using Equibles.CommonStocks.Data.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

/// <summary>
/// What OpenFIGI states about the US line of a listed ISIN: its share class, its ticker(s), its security
/// type and whether the over-the-counter venue quotes it. Nothing is inferred from a ticker's spelling.
/// </summary>
public sealed record IsinShareClassAnswer(
    string ShareClassFigi,
    IReadOnlyList<string> UsTickers,
    string UsSecurityType,
    bool QuotedOverTheCounter,
    string PayloadJson
)
{
    // Bloomberg's exchange codes for the US composite and for the over-the-counter venue.
    public const string UsComposite = "US";
    public const string UsOverTheCounter = "UV";

    public static IsinShareClassAnswer Read(string isin, JArray composite, JArray overTheCounter)
    {
        var lines = Lines(composite, UsComposite);
        var quoted = Lines(overTheCounter, UsOverTheCounter);
        var tickers = lines
            .Select(Ticker)
            .Where(ticker => ticker != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var shareClass = Single(lines, "shareClassFIGI");
        // Every US ticker must be quoted there under the same share class; one missing leaves room for an exchange line.
        var quotedTickers = quoted
            .Where(line => shareClass != null && (string)line["shareClassFIGI"] == shareClass)
            .Select(Ticker)
            .ToHashSet(StringComparer.Ordinal);
        return new(
            shareClass,
            tickers,
            Single(lines, "securityType2"),
            tickers.Count > 0 && tickers.All(quotedTickers.Contains),
            new JObject
            {
                ["isin"] = isin,
                ["composite"] = new JArray(lines),
                ["overTheCounter"] = new JArray(quoted),
            }.ToString(Formatting.None)
        );
    }

    private static List<JObject> Lines(JArray answer, string exchangeCode) =>
        answer.OfType<JObject>().Where(line => (string)line["exchCode"] == exchangeCode).ToList();

    private static string Ticker(JObject line) =>
        TickerNormalizer.NormalizeListed(((string)line["ticker"])?.Replace('/', '-'));

    // The value every line states, or null when they state none or disagree.
    private static string Single(List<JObject> lines, string field)
    {
        var values = lines.Select(line => (string)line[field]).Distinct().ToList();
        return values.Count == 1 ? values[0] : null;
    }
}
