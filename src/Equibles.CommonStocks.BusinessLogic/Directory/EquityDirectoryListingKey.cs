namespace Equibles.CommonStocks.BusinessLogic.Directory;

public sealed record EquityDirectoryListingKey(
    string Isin,
    string MarketIdentifierCode,
    string Ticker
);
