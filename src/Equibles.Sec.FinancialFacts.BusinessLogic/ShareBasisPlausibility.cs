namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>
/// Decides whether two share counts for the same issuer can be statements of the same unit, and
/// whether a stored (market cap, share count) pair credibly sits on the listed-security basis.
/// Shared by the Yahoo key-stats sync and the financial-facts importer so the two writers of
/// <c>CommonStock.SharesOutStanding</c> apply one definition of "these figures are on different
/// bases" and can never fight each other across the same threshold.
///
/// The problem this solves: an issuer can list one security while its EDGAR cover page counts
/// another unit entirely. The form-based foreign-private-issuer guard catches 20-F/40-F filers,
/// but a former FPI that lost the status keeps filing 10-K/10-Q while its US listing is still an
/// ADS — AKTX files 10-Q covers of 91.6B ordinary shares against ~1.1M listed ADSs (80,000
/// ordinary per ADS since 2026-03-31). No API the importers ingest exposes the registered
/// security's title (the SEC companyfacts endpoint serves numeric facts only, and the submissions
/// feed has no security section), so the mismatch is detected from the figures themselves.
/// </summary>
public static class ShareBasisPlausibility
{
    /// <summary>
    /// The largest ratio two same-unit statements of an issuer's share count have ever been
    /// observed apart. Legitimate divergences are corporate-action lags — a price feed trailing a
    /// reverse split (COPR ~20x; even the most extreme real reverse splits stay around 1:100 to
    /// 1:250) or a merger/issuance one source has and the other hasn't. Different-unit pairs sit
    /// far above: ordinary-shares-vs-ADS bases run 458x to 80,000x (ABTC, CNDA, Latam Airlines,
    /// AKTX). 300 splits the two regimes with margin on both sides.
    /// </summary>
    public const double MaxPlausibleSameUnitRatio = 300;

    // A stored pair whose implied per-share price (market cap ÷ shares) falls inside these bounds
    // is credibly a real listed quote: the lower bound excludes a garbage-large stored count
    // against a sane market cap (implied price collapses to fractions of a cent), the upper bound
    // excludes a nominal-placeholder count — shells file cover pages of 1/100/1000 shares, so the
    // implied price explodes to millions per share. BRK.A, the most expensive real listing, is
    // ~$780k/share, an order of magnitude inside the upper bound. Sub-penny OTC listings fall
    // outside the lower bound and simply don't get the protection (unchanged behavior for them).
    private const double MinPlausibleSharePrice = 0.01;
    private const double MaxPlausibleSharePrice = 10_000_000;

    /// <summary>
    /// True when the two counts are too far apart to be statements of the same unit — one is an
    /// ordinary-share count against an ADS count, or one is garbage (dropped digit,
    /// thousands-scaled entry, nominal placeholder). Direction-agnostic: either count may be the
    /// larger. False when either count is missing (zero or negative) — absence is not evidence of
    /// a unit mismatch.
    /// </summary>
    public static bool IsUnitMismatch(long countA, long countB)
    {
        if (countA <= 0 || countB <= 0)
            return false;

        var ratio = (double)countA / countB;
        return ratio >= MaxPlausibleSameUnitRatio || ratio <= 1d / MaxPlausibleSameUnitRatio;
    }

    /// <summary>
    /// True when a stored (market cap, share count) pair implies a per-share price a real listed
    /// security could trade at — evidence the pair sits on the listed-security basis and deserves
    /// protection from an overwrite in a different unit. False for a missing market cap or share
    /// count, for the implied sub-cent price of a garbage-large count, and for the implied
    /// millions-per-share price of a nominal-placeholder count (1/100/1000 shares), all of which
    /// should be repaired by the authoritative EDGAR figure, not protected from it.
    /// </summary>
    public static bool ImpliesPlausibleSharePrice(double marketCapitalization, long shares)
    {
        if (marketCapitalization <= 0 || shares <= 0)
            return false;

        var impliedPrice = marketCapitalization / shares;
        return impliedPrice >= MinPlausibleSharePrice && impliedPrice <= MaxPlausibleSharePrice;
    }
}
