namespace Equibles.EquityMarkets.Data.Catalog;

// Code-owned like the index catalog: a market exists here before any row can name it, and every
// suffix, exchange code and time zone below was verified against the provider before being written.
public static class EquityMarketCatalog
{
    public static readonly IReadOnlyList<EquityMarket> All =
    [
        Euronext(
            "euronext-paris",
            "Euronext Paris",
            "FR",
            ["XPAR", "ALXP", "XMLI", "XPMC"],
            ".PA",
            "PAR",
            "Europe/Paris",
            "PAR"
        ),
        Euronext(
            "euronext-amsterdam",
            "Euronext Amsterdam",
            "NL",
            ["XAMS", "TNLA", "XAMC"],
            ".AS",
            "AMS",
            "Europe/Amsterdam",
            "AMS"
        ),
        Euronext(
            "euronext-brussels",
            "Euronext Brussels",
            "BE",
            ["XBRU", "ALXB", "ENXB", "MLXB", "TNLB"],
            ".BR",
            "BRU",
            "Europe/Brussels",
            "BRU"
        ),
        Euronext(
            "euronext-dublin",
            "Euronext Dublin",
            "IE",
            ["XMSM", "XESM", "XACD", "XATL"],
            ".IR",
            "ISE",
            "Europe/Dublin",
            "DUB",
            open: new(8, 0),
            close: new(16, 28),
            auction: new(16, 30)
        ),
        Euronext(
            "euronext-oslo",
            "Euronext Oslo",
            "NO",
            ["XOSL", "XOAS", "MERK"],
            ".OL",
            "OSL",
            "Europe/Oslo",
            "OSL",
            "NOK",
            close: new(16, 20),
            auction: new(16, 25)
        ),
        Euronext(
            "euronext-milan",
            "Euronext Milan",
            "IT",
            ["MTAA", "MTAH", "EXGM", "ETLX", "BGEM", "MIVX"],
            ".MI",
            "MIL",
            "Europe/Rome",
            "MIL"
        ),
        Euronext(
            "euronext-lisbon",
            "Euronext Lisbon",
            "PT",
            ["XLIS", "ENXL", "ALXL"],
            ".LS",
            "LIS",
            "Europe/Lisbon",
            "LIS",
            open: new(8, 0),
            close: new(16, 30),
            auction: new(16, 35)
        ),
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
                "XETR",
                "XETA",
                "XETB",
                "XETS",
                "XETU",
                "XETV",
                "XETW",
                "XFRA",
                "FRAA",
                "FRAB",
                "FRAS",
                "FRAV",
                "FRAW",
            ]
        ),
        // FIRDS files a Nasdaq main-market share under the lit book and its Nordic@Mid and Auction on Demand
        // segments, and a First North share under those two segments and the SME growth-market code; the home is any.
        Nasdaq(
            "nasdaq-stockholm",
            "Nasdaq Stockholm",
            "SE",
            ["XSTO", "FNSE"],
            ["XSTO", "DSTO", "MSTO", "FNSE", "DNSE", "MNSE", "SSME"],
            "SEK",
            ".ST",
            "STO",
            "Europe/Stockholm",
            new(9, 0),
            new(17, 25),
            new(17, 30)
        ),
        Nasdaq(
            "nasdaq-helsinki",
            "Nasdaq Helsinki",
            "FI",
            ["XHEL", "FNFI"],
            ["XHEL", "DHEL", "MHEL", "FNFI", "DNFI", "MNFI", "FSME"],
            "EUR",
            ".HE",
            "HEL",
            "Europe/Helsinki",
            new(10, 0),
            new(18, 25),
            new(18, 30)
        ),
        Nasdaq(
            "nasdaq-copenhagen",
            "Nasdaq Copenhagen",
            "DK",
            ["XCSE", "FNDK"],
            ["XCSE", "DCSE", "MCSE", "FNDK", "DNDK", "MNDK", "DSME"],
            "DKK",
            ".CO",
            "CPH",
            "Europe/Copenhagen",
            new(9, 0),
            new(16, 55),
            new(17, 0)
        ),
        // FIRDS also files a Madrid share on DMAD, the continuous market's dark midpoint book.
        new(
            "bme",
            "Bolsas y Mercados Españoles",
            "ES",
            ["XMAD"],
            "EUR",
            ".MC",
            "MCE",
            "Europe/Madrid",
            new(9, 0),
            new(17, 30),
            new(17, 35),
            DirectorySource: "bme",
            DelayedTradeSource: null,
            DelayedTradeLocationCode: null,
            FirdsVenueCodes: ["XMAD", "DMAD"],
            HomeVenueCodes: ["XMAD", "DMAD"]
        ),
        new(
            "gpw",
            "Warsaw Stock Exchange",
            "PL",
            ["XWAR"],
            "PLN",
            ".WA",
            "WSE",
            "Europe/Warsaw",
            new(9, 0),
            new(17, 0),
            new(17, 5),
            DirectorySource: "gpw",
            DelayedTradeSource: null,
            DelayedTradeLocationCode: null
        ),
        // Last on purpose: the FCA register homes the EEA issuers' London lines too, so their EEA directories
        // run first and hold the presentation before a London listing of the same share arrives. AIM is its own
        // venue code, so the growth market is a market identifier here rather than a segment of XLON.
        new(
            "lse",
            "London Stock Exchange",
            "GB",
            ["XLON", "AIMX"],
            "GBP",
            ".L",
            "LSE",
            "Europe/London",
            new(8, 0),
            new(16, 30),
            new(16, 35),
            DirectorySource: "lse",
            DelayedTradeSource: null,
            DelayedTradeLocationCode: null,
            FirdsAuthority: "FCA"
        ),
    ];

    private static readonly IReadOnlyDictionary<string, EquityMarket> ByCode = All.ToDictionary(
        market => market.Code,
        StringComparer.Ordinal
    );

    private static readonly IReadOnlyDictionary<string, EquityMarket> ByMic = All.SelectMany(
            market => market.MarketIdentifierCodes.Select(mic => (mic, market))
        )
        .ToDictionary(pair => pair.mic, pair => pair.market, StringComparer.Ordinal);

    public static EquityMarket TryGet(string code) =>
        code != null && ByCode.TryGetValue(code, out var market) ? market : null;

    public static EquityMarket ByMarketIdentifierCode(string marketIdentifierCode) =>
        marketIdentifierCode != null && ByMic.TryGetValue(marketIdentifierCode, out var market)
            ? market
            : null;

    // Session times are the venue's own prints: continuous trading opens at SessionOpen, the last continuous print
    // lands before SessionClose and the closing auction uncrosses at ClosingAuctionEnd (trade-at-last follows it).
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
        TimeOnly? open = null,
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
            open ?? new(9, 0),
            close ?? new(17, 30),
            auction ?? new(17, 35),
            DirectorySource: "euronext",
            DelayedTradeSource: "euronext",
            DelayedTradeLocationCode: location
        );

    // The directory names a main-market or First North line by its exchange; the lit MIC of each is what the
    // adapter states and the segment codes are where FIRDS files the same line.
    private static EquityMarket Nasdaq(
        string code,
        string name,
        string country,
        string[] mics,
        string[] venues,
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
            DirectorySource: "nasdaq-nordic",
            DelayedTradeSource: null,
            DelayedTradeLocationCode: null,
            FirdsVenueCodes: venues,
            HomeVenueCodes: venues
        );
}
