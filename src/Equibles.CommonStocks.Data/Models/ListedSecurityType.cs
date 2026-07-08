using System.ComponentModel.DataAnnotations;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// What kind of security a stock row's ticker actually is, classified from the
/// issuer's SEC cover-page 12(b) registration table (<c>dei:Security12bTitle</c>
/// matched to the row's ticker via <c>dei:TradingSymbol</c>) — the authoritative
/// statement of each listed security's title, never a ticker or name heuristic.
/// The stock universe is keyed by tickers carrying FINRA/price data, so a row can
/// be an issuer's exchange-traded note or preferred series rather than its common
/// equity (e.g. a bankrupt issuer whose only listed security is a baby bond);
/// per-share figures computed against such a row's common-share record are
/// meaningless, and equity-only surfaces exclude the unambiguously non-equity
/// kinds (<see cref="PreferredShares"/>, <see cref="DebtSecurities"/>,
/// <see cref="Warrants"/>, <see cref="Rights"/>). <see cref="Units"/> stays
/// included: MLP common units (Energy Transfer, Enterprise Products) are genuine
/// operating equity, indistinguishable by title from fund units.
/// </summary>
public enum ListedSecurityType
{
    /// <summary>
    /// No 12(b) registration row matches the ticker — the issuer lists nothing on
    /// an exchange (OTC / 12(g) registrants), the cover page has not been
    /// extracted yet, or the ticker on record differs from the filed symbol.
    /// Treated as equity by surfaces: exclusion requires positive evidence.
    /// </summary>
    [Display(Name = "Unknown")]
    Unknown = 0,

    /// <summary>Common stock, ordinary shares, ADS/ADR, or REIT shares of beneficial interest.</summary>
    [Display(Name = "Common shares")]
    CommonShares = 1,

    /// <summary>A preferred/preference series, including depositary shares representing one.</summary>
    [Display(Name = "Preferred shares")]
    PreferredShares = 2,

    /// <summary>Exchange-traded debt — notes, debentures, or bonds (baby bonds).</summary>
    [Display(Name = "Debt securities")]
    DebtSecurities = 3,

    [Display(Name = "Warrants")]
    Warrants = 4,

    [Display(Name = "Rights")]
    Rights = 5,

    /// <summary>
    /// Units — SPAC units, fund/trust units, or MLP common units. Deliberately NOT
    /// excluded from equity surfaces: MLP common units are operating equity.
    /// </summary>
    [Display(Name = "Units")]
    Units = 6,

    /// <summary>A 12(b) title that fits no known kind; treated as equity, like <see cref="Unknown"/>.</summary>
    [Display(Name = "Other")]
    Other = 7,
}
