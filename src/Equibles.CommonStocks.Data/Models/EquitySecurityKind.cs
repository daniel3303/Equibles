namespace Equibles.CommonStocks.Data.Models;

// Identity from authoritative security reference data; a receipt is never an ordinary share.
public enum EquitySecurityKind
{
    Unknown = 0,
    OrdinaryShare = 1,
    PreferredShare = 2,
    DepositaryReceipt = 3,
    Other = 4,
}
