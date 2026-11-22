namespace Equibles.InsiderTrading.Mcp.Captures;

// One row of a GetInsiderTransactions answer as printed, with the filed values behind its number cells and whether it
// is an open-market purchase or sale.
public sealed record InsiderTransactionRow(
    DateOnly Date,
    DateOnly Filed,
    string Insider,
    string Role,
    string Type,
    bool OpenMarket,
    bool IsPurchase,
    decimal Shares,
    string SharesText,
    decimal Price,
    string PriceText,
    decimal Value,
    string ValueText,
    decimal OwnedAfter,
    string OwnedAfterText,
    string Security,
    string Ownership,
    string Plan
);
