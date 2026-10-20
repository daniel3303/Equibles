namespace Equibles.CommonStocks.Data.Models;

// Identity from authoritative security reference data; a receipt is never an ordinary share.
public enum EquitySecurityKind
{
    Unknown = 0,
    OrdinaryShare = 1,
    PreferredShare = 2,
    DepositaryReceipt = 3,
    Other = 4,

    // A listed unit of an operating trust (a REIT or income trust); it is neither a share nor a receipt.
    TrustUnit = 5,
}
