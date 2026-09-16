namespace Equibles.Integrations.NasdaqNordic;

// One Nordic exchange the screener serves: the market code names the list and each category's exchange label
// is what the instrument reply must state for a line of that list.
public sealed record NasdaqNordicMarket(
    string Slug,
    string MarketCode,
    string MainMarketIdentifierCode,
    string MainMarketExchange,
    string FirstNorthIdentifierCode,
    string FirstNorthExchange
)
{
    private const string AuctionSuffix = " Auction";

    public static readonly NasdaqNordicMarket Stockholm = new(
        "stockholm",
        "STO",
        "XSTO",
        "Nasdaq Stockholm",
        "FNSE",
        "First North GM Sweden"
    );
    public static readonly NasdaqNordicMarket Helsinki = new(
        "helsinki",
        "HEL",
        "XHEL",
        "Nasdaq Helsinki",
        "FNFI",
        "First North GM Finland"
    );
    public static readonly NasdaqNordicMarket Copenhagen = new(
        "copenhagen",
        "CPH",
        "XCSE",
        "Nasdaq Copenhagen",
        "FNDK",
        "First North GM Denmark"
    );

    public static readonly IReadOnlyList<NasdaqNordicMarket> All =
    [
        Stockholm,
        Helsinki,
        Copenhagen,
    ];

    public static readonly IReadOnlyList<NasdaqNordicCategory> Categories =
    [
        NasdaqNordicCategory.MainMarket,
        NasdaqNordicCategory.FirstNorth,
    ];

    public static NasdaqNordicMarket FromSlug(string slug) =>
        All.FirstOrDefault(market => market.Slug == slug);

    public string MarketIdentifierCode(NasdaqNordicCategory category) =>
        category == NasdaqNordicCategory.MainMarket
            ? MainMarketIdentifierCode
            : FirstNorthIdentifierCode;

    public string ExchangeLabel(NasdaqNordicCategory category) =>
        category == NasdaqNordicCategory.MainMarket ? MainMarketExchange : FirstNorthExchange;

    // A line too illiquid for continuous trading is quoted in periodic auctions, and the instrument reply names
    // the trading model after the exchange: the venue is the same one, so both spellings confirm the same list.
    public IReadOnlyList<string> ExchangeLabels(NasdaqNordicCategory category) =>
        [ExchangeLabel(category), ExchangeLabel(category) + AuctionSuffix];

    public NasdaqNordicCategory? CategoryOf(string marketIdentifierCode) =>
        marketIdentifierCode == MainMarketIdentifierCode ? NasdaqNordicCategory.MainMarket
        : marketIdentifierCode == FirstNorthIdentifierCode ? NasdaqNordicCategory.FirstNorth
        : null;
}

public enum NasdaqNordicCategory
{
    MainMarket,
    FirstNorth,
}
