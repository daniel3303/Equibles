namespace Equibles.CorporateActions.BusinessLogic;

public sealed record PendingPriceReconciliationSeries(
    Guid EquityIssuerId,
    string ListedTicker,
    IReadOnlyList<PendingSplitSnapshot> Splits,
    IReadOnlyList<PendingDividendSnapshot> Dividends,
    Guid EquityListingId
);
