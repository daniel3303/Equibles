using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Chooses the SEC ticker that presents an issuer. SEC's ticker file order is the default, but
/// it sometimes lists a warrant, unit, preferred series or note ahead of the common stock
/// (HeartBeam ships BEATW before BEAT), so the issuer's own 12(b) registration titles may
/// override it.
/// </summary>
public static class UsPresentationTicker
{
    /// <summary>
    /// Returns SEC's first eligible ticker unless the issuer's filing positively classifies it as
    /// a non-common security. Then the first sibling filed as common stock wins; failing that, a
    /// debt, preferred, warrant or rights first ticker yields to the first sibling not filed as
    /// one of those kinds, since surfaces treat an unclassified listing as equity. An
    /// unclassified first ticker always keeps SEC order.
    /// </summary>
    public static string Choose(
        IReadOnlyList<string> tickers,
        IReadOnlyDictionary<string, ListedSecurityType> filedTypes
    )
    {
        var eligible = tickers
            .Where(ticker => ticker != null && ticker.Length <= TickerNormalizer.MaxPrimaryLength)
            .ToList();
        if (eligible.Count == 0)
            return null;
        if (filedTypes == null || filedTypes.Count == 0)
            return eligible[0];
        if (!IsNonCommon(FiledType(eligible[0], filedTypes)))
            return eligible[0];
        // A units first ticker is not non-equity, so the fallback below can only return it.
        return eligible.FirstOrDefault(ticker =>
                FiledType(ticker, filedTypes) == ListedSecurityType.CommonShares
            )
            ?? eligible.FirstOrDefault(ticker => !IsNonEquity(FiledType(ticker, filedTypes)))
            ?? eligible[0];
    }

    private static ListedSecurityType FiledType(
        string ticker,
        IReadOnlyDictionary<string, ListedSecurityType> filedTypes
    )
    {
        var identity = TickerNormalizer.NormalizeIdentity(ticker);
        return identity != null && filedTypes.TryGetValue(identity, out var type)
            ? type
            : ListedSecurityType.Unknown;
    }

    // Units stay demotable here even though equity surfaces keep them: an MLP files no common
    // sibling, so only a SPAC unit with separately listed shares is ever overridden.
    private static bool IsNonCommon(ListedSecurityType type) =>
        type == ListedSecurityType.Units || IsNonEquity(type);

    // The kinds equity surfaces exclude; any other sibling can present the issuer's equity.
    private static bool IsNonEquity(ListedSecurityType type) =>
        type
            is ListedSecurityType.Warrants
                or ListedSecurityType.Rights
                or ListedSecurityType.PreferredShares
                or ListedSecurityType.DebtSecurities;
}
