namespace Equibles.CorporateActions.BusinessLogic;

internal readonly record struct PriceReconciliationKey(Guid CommonStockId, string ListedTicker);
