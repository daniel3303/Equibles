namespace Equibles.CommonStocks.Data.Models;

// A resolved source symbol plus its native listing and issuer, carried across an import boundary.
public readonly record struct EquityListingReference(
    Guid EquityListingId,
    Guid EquityIssuerId,
    string ListedTicker
);
