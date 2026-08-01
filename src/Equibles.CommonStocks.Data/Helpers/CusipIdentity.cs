namespace Equibles.CommonStocks.Data.Helpers;

/// <summary>
/// Structural facts about a CUSIP. The first 6 characters identify the ISSUER and the
/// next 2 the specific issue, so two CUSIPs sharing a prefix are two securities of one
/// issuer (AMC's 00165C104 and 00165C302 across its 2023 reverse split) while a differing
/// prefix means different issuers.
/// <para>
/// This is the check that makes a retired-CUSIP backfill safe against TICKER RECYCLING:
/// a symbol freed by a delisted issuer and reassigned years later would otherwise carry
/// the dead issuer's CUSIP onto whichever company holds the symbol now, and its 13F
/// positions with it.
/// </para>
/// </summary>
public static class CusipIdentity
{
    private const int IssuerLength = 6;

    /// <summary>
    /// The 6-character issuer prefix, or null when the value is too short to carry one.
    /// </summary>
    public static string Issuer(string cusip)
    {
        var trimmed = cusip?.Trim();
        return trimmed == null || trimmed.Length < IssuerLength
            ? null
            : trimmed[..IssuerLength].ToUpperInvariant();
    }

    /// <summary>
    /// Whether both CUSIPs name the same issuer. False when either is missing or
    /// malformed — an unknown issuer is never assumed to match.
    /// </summary>
    public static bool SameIssuer(string cusip, string other)
    {
        var issuer = Issuer(cusip);
        return issuer != null && issuer == Issuer(other);
    }
}
