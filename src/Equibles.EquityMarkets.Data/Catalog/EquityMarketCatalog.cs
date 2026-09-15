namespace Equibles.EquityMarkets.Data.Catalog;

// Code-owned like the index catalog: a market exists here before any row can name it, and every
// suffix, exchange code and time zone below was verified against the provider before being written.
public static class EquityMarketCatalog
{
    public static readonly IReadOnlyList<EquityMarket> All =
    [
        Euronext("euronext-paris", "Euronext Paris", "FR", ["XPAR", "ALXP", "XMLI", "XPMC"], ".PA", "PAR", "Europe/Paris", "PAR"),
        Euronext("euronext-amsterdam", "Euronext Amsterdam", "NL", ["XAMS", "TNLA", "XAMC"], ".AS", "AMS", "Europe/Amsterdam", "AMS"),
        Euronext("euronext-brussels", "Euronext Brussels", "BE", ["XBRU", "ALXB", "ENXB", "MLXB", "TNLB"], ".BR", "BRU", "Europe/Brussels", "BRU"),
        Euronext("euronext-dublin", "Euronext Dublin", "IE", ["XMSM", "XESM", "XACD", "XATL"], ".IR", "ISE", "Europe/Dublin", "DUB"),
        Euronext("euronext-oslo", "Euronext Oslo", "NO", ["XOSL", "XOAS", "MERK"], ".OL", "OSL", "Europe/Oslo", "OSL", "NOK"),
        Euronext("euronext-milan", "Euronext Milan", "IT", ["MTAA", "MTAH", "EXGM", "ETLX", "BGEM", "MIVX"], ".MI", "MIL", "Europe/Rome", "MIL"),
        Euronext("euronext-lisbon", "Euronext Lisbon", "PT", ["XLIS", "ENXL", "ALXL"], ".LS", "LIS", "Europe/Lisbon", "LIS", close: new(16, 30), auction: new(16, 35)),
        // FIRDS files Xetra by segment and places a German share's home on Xetra, the Frankfurt floor or a regional
        // exchange, so home spans Deutsche Börse's venues and the directory's own primary-market column decides.
        new(
            "xetra",
            "Xetra",
            "DE",
            ["XETR"],
            "EUR",
            ".DE",
            "GER",
            "Europe/Berlin",
            new(9, 0),
            new(17, 30),
            new(17, 35),
            DirectorySource: "xetra",
            DelayedTradeSource: null,
            DelayedTradeLocationCode: null,
            FirdsVenueCodes: ["XETA", "XETB", "XETS"],
            HomeVenueCodes:
            [
                "XETR", "XETA", "XETB", "XETS", "XETU", "XETV", "XETW",
                "XFRA", "FRAA", "FRAB", "FRAS", "FRAV", "FRAW",
            ]
        ),
        Pending("nasdaq-stockholm", "Nasdaq Stockholm", "SE", ["XSTO", "FNSE"], "SEK", ".ST", "STO", "Europe/Stockholm", new(9, 0), new(17, 25), new(17, 30)),
        Pending("nasdaq-helsinki", "Nasdaq Helsinki", "FI", ["XHEL"], "EUR", ".HE", "HEL", "Europe/Helsinki", new(10, 0), new(18, 25), new(18, 30)),
        Pending("nasdaq-copenhagen", "Nasdaq Copenhagen", "DK", ["XCSE"], "DKK", ".CO", "CPH", "Europe/Copenhagen", new(9, 0), new(16, 55), new(17, 0)),
        Pending("lse", "London Stock Exchange", "GB", ["XLON"], "GBP", ".L", "LSE", "Europe/London", new(8, 0), new(16, 30), new(16, 35)),
        Pending("bme", "Bolsas y Mercados Españoles", "ES", ["XMAD"], "EUR", ".MC", "MCE", "Europe/Madrid", new(9, 0), new(17, 30), new(17, 35)),
        Pending("gpw", "Warsaw Stock Exchange", "PL", ["XWAR"], "PLN", ".WA", "WSE", "Europe/Warsaw", new(9, 0), new(17, 0), new(17, 5)),
    ];

    private static readonly IReadOnlyDictionary<string, EquityMarket> ByCode = All.ToDictionary(
        market => market.Code,
        StringComparer.Ordinal
    );

    private static readonly IReadOnlyDictionary<string, EquityMarket> ByMic = All.SelectMany(market =>
            market.MarketIdentifierCodes.Select(mic => (mic, market))
        )
        .ToDictionary(pair => pair.mic, pair => pair.market, StringComparer.Ordinal);

    public static EquityMarket TryGet(string code) =>
        code != null && ByCode.TryGetValue(code, out var market) ? market : null;

    public static EquityMarket ByMarketIdentifierCode(string marketIdentifierCode) =>
        marketIdentifierCode != null && ByMic.TryGetValue(marketIdentifierCode, out var market)
            ? market
            : null;

    private static EquityMarket Euronext(
        string code,
        string name,
        string country,
        string[] mics,
        string suffix,
        string exchange,
        string timeZone,
        string location,
        string currency = "EUR",
        TimeOnly? close = null,
        TimeOnly? auction = null
    ) =>
        new(
            code,
            name,
            country,
            mics,
            currency,
            suffix,
            exchange,
            timeZone,
            new(9, 0),
            close ?? new(17, 30),
            auction ?? new(17, 35),
            DirectorySource: "euronext",
            DelayedTradeSource: "euronext",
            DelayedTradeLocationCode: location
        );

    // Catalogued so registrations, prices and pages can name the market before its directory adapter lands.
    private static EquityMarket Pending(
        string code,
        string name,
        string country,
        string[] mics,
        string currency,
        string suffix,
        string exchange,
        string timeZone,
        TimeOnly open,
        TimeOnly close,
        TimeOnly auction
    ) =>
        new(
            code,
            name,
            country,
            mics,
            currency,
            suffix,
            exchange,
            timeZone,
            open,
            close,
            auction,
            DirectorySource: null,
            DelayedTradeSource: null,
            DelayedTradeLocationCode: null
        );
}
