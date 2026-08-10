using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// Resolves the factor implied by captured splits between a historical as-of date and today.
/// </summary>
/// <remarks>
/// <para>
/// Historical figures quoted as-of a date (a 13F share count, a Form 4 per-share price) stay on
/// that date's basis. The factor returned here restates those figures across captured splits: an
/// as-of share count × factor lands on the present share-count basis.
/// </para>
/// <para>
/// This resolver checks split attribution and whether reconciliation was stamped applied. It does
/// not inspect price values and cannot prove a raw stored Close is on today's basis: Yahoo can
/// serve either basis during a full-history replacement. Do not use a successful resolution as
/// evidence for a universal raw-price basis. A pending or unattributed split is reported as
/// unresolvable (<c>false</c>) instead of guessed at.
/// </para>
/// </remarks>
public static class SplitBasisResolver
{
    /// <summary>
    /// Resolves the factor between figures quoted as-of <paramref name="asOfDate"/> and today's
    /// share-count basis. Returns <c>false</c> when a split that would move the figure has not had
    /// its price reconciliation stamped applied; <paramref name="factor"/> is then 1 and must not
    /// be used. A successful result does not prove the basis of raw stored price rows.
    /// <para>
    /// <paramref name="listedTicker"/> names the exact security; null means the filer's
    /// primary (<paramref name="primaryTicker"/>). A PRIMARY figure uses every split captured
    /// from its own series — unattributed legacy rows included, since only the primary series
    /// could produce them. A SECONDARY figure may only be moved by splits attributed to that
    /// listing — and while ANY post-date split of the issuer is attributed elsewhere (or to no
    /// listing), the class's own split history is unknowable from stored data, so the caller
    /// honestly defers rather than assuming the classes split together.
    /// </para>
    /// </summary>
    public static bool TryResolveFactor(
        DateOnly asOfDate,
        IReadOnlyList<StockSplit> splits,
        string listedTicker,
        string primaryTicker,
        IReadOnlyCollection<string> secondaryTickers,
        out decimal factor
    )
    {
        factor = 1m;

        if (splits == null || splits.Count == 0)
        {
            return true;
        }

        var positionSeries = listedTicker ?? primaryTicker;
        var resolved = 1m;
        foreach (var split in splits)
        {
            // A figure dated on the effective date is already post-split, so the comparison is
            // strict — the same boundary SplitAdjustment.ShareCountFactor uses.
            if (split.EffectiveDate <= asOfDate)
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
                // Another listing of the same issuer split after the as-of date: for a
                // secondary it means the class's own basis cannot be established from
                // stored data — defer.
                if (listedTicker != null)
                {
                    return false;
                }

                // For the primary, a split attributed to a KNOWN sibling listing moves
                // nothing here. But an attribution matching neither the current primary
                // nor any current secondary is a stale symbol (the primary renamed after
                // capture, and the attribution is preserved verbatim) — that split very
                // likely IS this series' own, so silently skipping it re-creates the
                // ratio-sized error this class exists to prevent. Unknown basis: defer.
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

            if (!split.IsPriceAdjustmentApplied())
            {
                return false;
            }

            // Mirrors SplitAdjustment: a non-positive denominator is a malformed ratio, skipped
            // rather than allowed to divide by zero. It cannot make the basis ambiguous because
            // it moves no figure.
            if (split.Denominator <= 0)
            {
                continue;
            }

            resolved *= split.Numerator / split.Denominator;
        }

        factor = resolved;
        return true;
    }
}
