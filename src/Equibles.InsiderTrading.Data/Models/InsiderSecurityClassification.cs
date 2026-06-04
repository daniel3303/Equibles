using System.Linq.Expressions;

namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// Classifies an insider transaction as an actual share transaction versus a
/// derivative (option, warrant, convertible note, etc.). The authoritative signal
/// is <see cref="InsiderSecurityKind"/>, parsed from the Form 4 table; rows not yet
/// reclassified (<see cref="InsiderSecurityKind.Unknown"/>) fall back to the
/// security-title keywords.
///
/// A derivative row's <c>PricePerShare</c> is the instrument's own price, so
/// Shares × PricePerShare is not a transaction dollar value and explodes any
/// value-based ranking. The dashboard value boards use <see cref="IsShareTransaction"/>
/// to exclude derivatives; the price validator reuses <see cref="IsDerivativeTitle"/>
/// for the same Unknown fallback so both share one definition.
/// </summary>
public static class InsiderSecurityClassification
{
    /// <summary>
    /// Substrings that identify a derivative security by its title (matched
    /// case-insensitively). Kept in sync with the literals spelled out in
    /// <see cref="IsShareTransaction"/> — EF Core can't translate an
    /// <c>Any</c>-over-array <c>Contains</c>, so the query form lists them inline.
    /// </summary>
    public static readonly string[] DerivativeTitleKeywords =
    {
        "option",
        "warrant",
        "right to buy",
        "right to sell",
        "convertible",
    };

    /// <summary>
    /// In-memory derivative test by title, used as the fallback for rows whose
    /// authoritative <see cref="InsiderSecurityKind"/> is still Unknown.
    /// </summary>
    public static bool IsDerivativeTitle(string securityTitle)
    {
        if (string.IsNullOrWhiteSpace(securityTitle))
            return false;
        return DerivativeTitleKeywords.Any(keyword =>
            securityTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>
    /// EF-translatable predicate selecting share (non-derivative) transactions:
    /// an authoritative NonDerivative kind, or an unclassified row whose title
    /// doesn't name a derivative. Derivative-kind rows are always excluded. The
    /// keyword literals mirror <see cref="DerivativeTitleKeywords"/>.
    /// </summary>
    public static readonly Expression<Func<InsiderTransaction, bool>> IsShareTransaction = t =>
        t.SecurityKind == InsiderSecurityKind.NonDerivative
        || (
            t.SecurityKind == InsiderSecurityKind.Unknown
            && (
                t.SecurityTitle == null
                || (
                    !t.SecurityTitle.ToLower().Contains("option")
                    && !t.SecurityTitle.ToLower().Contains("warrant")
                    && !t.SecurityTitle.ToLower().Contains("right to buy")
                    && !t.SecurityTitle.ToLower().Contains("right to sell")
                    && !t.SecurityTitle.ToLower().Contains("convertible")
                )
            )
        );
}
