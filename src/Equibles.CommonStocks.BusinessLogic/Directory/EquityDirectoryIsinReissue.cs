using Equibles.Core.Identity;

namespace Equibles.CommonStocks.BusinessLogic.Directory;

// A venue restates a stable code with a new ISIN after a split, a share consolidation or a capital reorganisation. Only
// the same issuer number proves the same issuer, so the bound security takes the new ISIN; anything else stays a
// conflict. CUSIP-numbered jurisdictions embed it as the CINS issuer number, and India's numbering agency as its issuer code.
internal static class EquityDirectoryIsinReissue
{
    public static bool Proves(string recordedIsin, string statedIsin) =>
        recordedIsin != null
        && statedIsin != null
        && recordedIsin != statedIsin
        && (
            SameIssuer(InternationalSecurityIdentifiers.CusipIssuerNumber, recordedIsin, statedIsin)
            || SameIssuer(
                InternationalSecurityIdentifiers.IndianIssuerCode,
                recordedIsin,
                statedIsin
            )
        );

    private static bool SameIssuer(
        Func<string, string> issuer,
        string recordedIsin,
        string statedIsin
    ) => issuer(recordedIsin) is { } number && number == issuer(statedIsin);
}
