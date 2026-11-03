namespace Equibles.CommonStocks.Data.Models;

// Identity from authoritative security reference data; a receipt is never an ordinary share.
public enum EquitySecurityKind
{
    Unknown = 0,
    OrdinaryShare = 1,
    PreferredShare = 2,
    DepositaryReceipt = 3,
    Other = 4,

    // A listed unit of an operating trust or limited partnership (a REIT, income trust or LP unit), not a share.
    TrustUnit = 5,

    // A unit of an exchange-traded fund (FIRDS CFI CE): one fund security listed on several venues in their currencies.
    ExchangeTradedFund = 6,

    // An exchange-traded commodity or note (FIRDS CFI EY a venue lists as a product): a debt security tracking an
    // underlying, never ownership of a company.
    ExchangeTradedNote = 7,
}
