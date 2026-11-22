namespace Equibles.InsiderTrading.Mcp.Captures;

// What one GetInsiderTransactions answer showed: the company, its rows newest first, and where they sit among the
// matching transactions.
public sealed record InsiderTransactionsCapture(
    string CompanyName,
    string Ticker,
    IReadOnlyList<InsiderTransactionRow> Rows,
    int Offset,
    int Total
);
