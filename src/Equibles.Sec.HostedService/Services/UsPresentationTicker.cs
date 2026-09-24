using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Chooses the SEC ticker that presents an issuer. SEC's ticker file order is the default, but
/// it sometimes lists a warrant, unit or preferred series ahead of the common stock (HeartBeam
/// ships BEATW before BEAT), so the issuer's own 12(b) registration titles may override it.
/// </summary>
public static class UsPresentationTicker
{
    /// <summary>
    /// Returns SEC's first eligible ticker unless the issuer's filing positively classifies that
    /// ticker as a non-common security AND positively classifies a listed sibling as common
    /// stock; then the first such sibling in SEC order wins. Unclassified tickers never override.
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
        return eligible.FirstOrDefault(ticker =>
                FiledType(ticker, filedTypes) == ListedSecurityType.CommonShares
            ) ?? eligible[0];
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
        type
            is ListedSecurityType.Warrants
                or ListedSecurityType.Rights
                or ListedSecurityType.Units
                or ListedSecurityType.PreferredShares
                or ListedSecurityType.DebtSecurities;
}
