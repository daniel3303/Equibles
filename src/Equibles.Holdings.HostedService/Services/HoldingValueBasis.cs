using Equibles.CorporateActions.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reconciles the two share bases a derived holding value multiplies together.
/// </summary>
/// <remarks>
/// <para>
/// A holding's <c>Value</c> is derived as shares × the period's closing price, but the two factors
/// are quoted on different bases. The share count is <b>as filed</b>: it is whatever the issuer's
/// shares were on the report date. The price is whatever <c>DailyStockPrice</c> holds <b>today</b>,
/// and the split-price reconciliation deliberately rewrites a stock's whole stored history onto
/// today's post-split basis whenever a split lands. Multiplying an as-filed count by a restated
/// price is a units error, and it is off by exactly the split ratio: Scion's 633,959 BioAtla shares
/// priced after that stock's 1:50 reverse split read as a $43.4M position against a filed $868,524.
/// A forward split fails the same way in the other direction — NVDA's pre-10:1 counts against
/// today's adjusted closes understate every 2023 position tenfold.
/// </para>
/// <para>
/// The fix is to restate the count onto the price's basis before multiplying, which is what
/// <see cref="SplitAdjustment.ShareCountFactor"/> computes. The factor is returned rather than
/// applied so the caller can fold it into the value product without rounding the share count to
/// whole shares first — on a reverse split that rounding is the difference between reproducing the
/// filed value exactly and missing it.
/// </para>
/// <para>
/// The restatement is only valid while the stored prices really are on today's basis. Between a
/// split being captured and its price reconciliation running, a stock's series is a mix of both
/// bases, and adjusting the count then would double-apply the ratio. That window is reported as
/// untrustworthy instead of guessed at: the caller leaves the row pending, and the repricing lane
/// values it once the reconciliation has stamped the split.
/// </para>
/// </remarks>
internal static class HoldingValueBasis
{
    /// <summary>
    /// Resolves the factor that restates a share count reported as-of <paramref name="reportDate"/>
    /// onto the basis the stored price series uses. Returns <c>false</c> when a split that would
    /// move the count has not had its price adjustment applied yet, meaning the stored prices
    /// straddle two bases and no honest value can be derived; <paramref name="shareCountFactor"/>
    /// is then 1 and must not be used.
    /// <para>
    /// <paramref name="listedTicker"/> names the exact security being valued; null is the
    /// filer's primary (<paramref name="primaryTicker"/>). A PRIMARY position uses every split
    /// captured from its own series — unattributed legacy rows included, since only the primary
    /// series could produce them. A SECONDARY position's count may only be moved by splits
    /// attributed to that listing — and while ANY post-report split of the issuer is attributed
    /// elsewhere (or to no listing), the class's own split history is unknowable from stored
    /// data, so the row honestly stays pending rather than getting a value that assumes the
    /// classes split together. Per-class split capture is the deferred follow-up that drains
    /// those pendings.
    /// </para>
    /// </summary>
    internal static bool TryResolveShareCountFactor(
        DateOnly reportDate,
        IReadOnlyList<StockSplit> splits,
        string listedTicker,
        string primaryTicker,
        IReadOnlyCollection<string> secondaryTickers,
        out decimal shareCountFactor
    )
    {
        shareCountFactor = 1m;

        if (splits == null || splits.Count == 0)
        {
            return true;
        }

        var positionSeries = listedTicker ?? primaryTicker;
        var factor = 1m;
        foreach (var split in splits)
        {
            // A report dated on the effective date already counts post-split shares, so the
            // comparison is strict — the same boundary SplitAdjustment.ShareCountFactor uses.
            if (split.EffectiveDate <= reportDate)
            {
                continue;
            }

            var belongsToSeries =
                split.PriceSeriesTicker == null
                    ? listedTicker == null
                    : string.Equals(
                        split.PriceSeriesTicker,
                        positionSeries,
                        StringComparison.OrdinalIgnoreCase
                    );
            if (!belongsToSeries)
            {
                // Another listing of the same issuer split after the report date: for a
                // secondary it means the class's own basis cannot be established from
                // stored data — stay pending.
                if (listedTicker != null)
                {
                    return false;
                }

                // For the primary, a split attributed to a KNOWN sibling listing moves
                // nothing here. But an attribution matching neither the current primary
                // nor any current secondary is a stale symbol (the primary renamed after
                // capture, and the attribution is preserved verbatim) — that split very
                // likely IS this series' own, so silently skipping it re-creates the
                // ratio-sized error this class exists to prevent. Unknown basis: pending.
                var attributedToKnownSibling =
                    secondaryTickers != null
                    && secondaryTickers.Contains(
                        split.PriceSeriesTicker,
                        StringComparer.OrdinalIgnoreCase
                    );
                if (!attributedToKnownSibling)
                {
                    return false;
                }
                continue;
            }

            if (split.PriceAdjustmentAppliedTime == null)
            {
                return false;
            }

            // Mirrors SplitAdjustment: a non-positive denominator is a malformed ratio, skipped
            // rather than allowed to divide by zero. It cannot make the basis ambiguous because it
            // moves no count.
            if (split.Denominator <= 0)
            {
                continue;
            }

            factor *= split.Numerator / split.Denominator;
        }

        shareCountFactor = factor;
        return true;
    }
}
