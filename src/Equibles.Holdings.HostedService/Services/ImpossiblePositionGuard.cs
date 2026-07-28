namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Rejects the dollar value of a position that cannot exist: one holder reporting more shares
/// than the issuer has.
/// </summary>
/// <remarks>
/// <para>
/// A holding's <c>Value</c> is always DERIVED as shares × the closing price, never taken from the
/// filing, so a share count in the wrong units is silently multiplied into a wrong dollar figure
/// that every AUM ranking then treats as real. The live case: a Schedule 13D/A reported
/// 32,098,694,296 Class A ordinary shares of NaaS — accurately, at 55.9% of the class — but NaaS
/// lists in the US as an American Depositary Share worth 3,200 ordinary shares, so pricing an
/// ordinary-share count at the ADS price produced a $100.8B position in a $38M company.
/// </para>
/// <para>
/// The test is arithmetic rather than a judgment: a single holder cannot own several times the
/// company. It deliberately does NOT try to recover the true value — the depositary ratio is not
/// modelled, and inventing one would replace a wrong number with a different wrong number. The
/// position keeps its reported share count (which is what the filer actually said) and loses only
/// the derived dollar figure, on the same principle as the day-change basis: a missing value is
/// honest, a wrong one is not.
/// </para>
/// </remarks>
internal static class ImpossiblePositionGuard
{
    /// <summary>
    /// How far past the issuer's shares outstanding a position may sit before it is impossible.
    /// Not 1×, because the two figures come from different dates: the share count is today's while
    /// the holding is a quarter-end position, and a buyback or a reverse split between them is
    /// ordinary. Nothing legitimate reaches 2× — the rows this catches are 100× to 11,000×.
    /// </summary>
    internal const long SharesOutstandingMultiple = 2;

    /// <summary>
    /// The widest and narrowest per-share price treated as a real listing when sanity-checking the
    /// anchor below. Deliberately generous: it exists to reject a nonsense share count, not to
    /// judge a share price.
    /// </summary>
    internal const double MinimumPlausibleSharePrice = 0.01;
    internal const double MaximumPlausibleSharePrice = 10_000;

    /// <summary>
    /// True when the position reports more shares than the issuer plausibly has, so its derived
    /// value must not be published.
    /// </summary>
    internal static bool ExceedsTheIssuer(
        long shares,
        long sharesOutstanding,
        double marketCapitalization
    )
    {
        if (shares <= 0)
        {
            return false;
        }

        if (!AnchorIsTrustworthy(sharesOutstanding, marketCapitalization))
        {
            return false;
        }

        return shares > sharesOutstanding * SharesOutstandingMultiple;
    }

    /// <summary>
    /// Whether the issuer's share count can be used to judge a position at all.
    /// </summary>
    /// <remarks>
    /// <c>CommonStock.SharesOutStanding</c> is itself wrong for a handful of stocks — Air Lease
    /// carries 200 shares beside a correct $7.28B market cap — and a guard anchored on a corrupt
    /// count deletes real data: that one stock accounts for 7,309 of the 7,749 rows a naive
    /// shares-outstanding rule matches, every one of them a legitimate holding. So the count has
    /// to agree with the market cap stored next to it before it is trusted: the implied per-share
    /// price must look like a share price. When it doesn't, the issuer is simply not judged — the
    /// guard stays silent rather than guessing which of the two figures is the broken one.
    /// </remarks>
    internal static bool AnchorIsTrustworthy(long sharesOutstanding, double marketCapitalization)
    {
        if (sharesOutstanding <= 0 || marketCapitalization <= 0)
        {
            return false;
        }

        var impliedSharePrice = marketCapitalization / sharesOutstanding;
        return impliedSharePrice >= MinimumPlausibleSharePrice
            && impliedSharePrice <= MaximumPlausibleSharePrice;
    }
}
