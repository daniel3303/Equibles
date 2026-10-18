using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.Data.Extensions;

public static class ListedSecurityTypeExtensions
{
    // Kinds a filing positively classifies as not common equity; Unknown, Other and Units stay equity.
    public static bool IsNonEquity(this ListedSecurityType type) =>
        type
            is ListedSecurityType.PreferredShares
                or ListedSecurityType.DebtSecurities
                or ListedSecurityType.Warrants
                or ListedSecurityType.Rights;
}
