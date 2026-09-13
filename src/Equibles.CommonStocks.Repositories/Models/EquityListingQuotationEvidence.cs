namespace Equibles.CommonStocks.Repositories.Models;

public sealed record EquityListingQuotationEvidence(
    EquityListingSourceBinding Binding,
    string Source,
    string PayloadJson
);
