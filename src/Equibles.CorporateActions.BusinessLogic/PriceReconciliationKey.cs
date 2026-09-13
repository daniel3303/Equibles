namespace Equibles.CorporateActions.BusinessLogic;

internal readonly record struct PriceReconciliationKey(
    Guid EquityIssuerId,
    string ListedTicker,
    Guid EquityListingId
);
