namespace Equibles.CorporateActions.BusinessLogic;

public sealed record PendingPriceReconciliationSeries(
    Guid CommonStockId,
    string ListedTicker,
    IReadOnlyList<PendingSplitSnapshot> Splits,
    IReadOnlyList<PendingDividendSnapshot> Dividends
);
