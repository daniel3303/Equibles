namespace Equibles.Integrations.Euronext;

// Euronext publishes one directory per location; the slug is the URL segment and the MIC set is what
// the gateway must cover exactly, or the snapshot is filtered and cannot withdraw absent listings.
public sealed record EuronextMarket(
    string Slug,
    IReadOnlySet<string> MarketIdentifierCodes,
    string TradesLocationCode
)
{
    private static readonly Uri Origin = new("https://live.euronext.com");

    public static readonly EuronextMarket Paris = new(
        "paris",
        Set("ALXP", "XMLI", "XPAR", "XPMC"),
        "PAR"
    );
    public static readonly EuronextMarket Amsterdam = new(
        "amsterdam",
        Set("TNLA", "XAMC", "XAMS"),
        "AMS"
    );
    public static readonly EuronextMarket Brussels = new(
        "brussels",
        Set("ALXB", "ENXB", "MLXB", "TNLB", "XBRU"),
        "BRU"
    );
    public static readonly EuronextMarket Dublin = new(
        "dublin",
        Set("XACD", "XATL", "XESM", "XMSM"),
        "DUB"
    );
    public static readonly EuronextMarket Oslo = new("oslo", Set("MERK", "XOAS", "XOSL"), "OSL");
    public static readonly EuronextMarket Milan = new(
        "milan",
        Set("BGEM", "ETLX", "EXGM", "MIVX", "MTAA", "MTAH"),
        "MIL"
    );
    public static readonly EuronextMarket Lisbon = new(
        "lisbon",
        Set("ALXL", "ENXL", "XLIS"),
        "LIS"
    );

    public static readonly IReadOnlyList<EuronextMarket> All =
    [
        Paris,
        Amsterdam,
        Brussels,
        Dublin,
        Oslo,
        Milan,
        Lisbon,
    ];

    public static EuronextMarket FromSlug(string slug) =>
        All.FirstOrDefault(market => market.Slug == slug);

    public static EuronextMarket ByMarketIdentifierCode(string mic) =>
        mic == null
            ? null
            : All.FirstOrDefault(market => market.MarketIdentifierCodes.Contains(mic));

    public Uri DirectoryUrl => new(Origin, $"/en/markets/{Slug}/equities/list");
    public string GatewayPath => $"/en/product_directory/data/stocks-{Slug}";

    private static IReadOnlySet<string> Set(params string[] mics) =>
        new HashSet<string>(mics, StringComparer.Ordinal);
}
