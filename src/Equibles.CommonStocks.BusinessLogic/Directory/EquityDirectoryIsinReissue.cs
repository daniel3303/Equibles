using Equibles.Core.Identity;

namespace Equibles.CommonStocks.BusinessLogic.Directory;

// A venue restates a stable code with a new ISIN after a share consolidation or capital reorganisation. Only the same
// CUSIP issuer number proves the same issuer, so the bound security takes the new ISIN; anything else stays a conflict.
internal static class EquityDirectoryIsinReissue
{
    public static bool Proves(string recordedIsin, string statedIsin) =>
        recordedIsin != null
        && statedIsin != null
        && recordedIsin != statedIsin
        && InternationalSecurityIdentifiers.CusipIssuerNumber(recordedIsin) is { } issuerNumber
        && issuerNumber == InternationalSecurityIdentifiers.CusipIssuerNumber(statedIsin);
}
