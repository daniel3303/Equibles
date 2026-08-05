using Equibles.Core.AutoWiring;
using Equibles.InsiderTrading.BusinessLogic.Models;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Decides whether an InsiderTransaction's reported per-share price is
/// plausible. Catches the recurring filer-error class where a Form 4 filer
/// types the total transaction value into <c>transactionPricePerShare</c>
/// (a per-share field), which then explodes the dashboard's Shares × Price
/// sort into nonsense numbers (trillions, quadrillions).
///
/// Stateless and pure — the daily bar and split basis are passed in by the
/// caller. Lookup is done by callers that have repository access (parser at
/// ingest time; backfill manager during recomputes).
///
/// The stored close is on TODAY'S split-adjusted basis while the filed price
/// is on the transaction date's basis, so every check runs on BOTH bases
/// (raw, and × the split factor). Comparing a pre-split price against the
/// adjusted close as if it were unadjusted once "repaired" 15,822 correct
/// prices into nonsense — AMZN's pre-20:1 $3,300.24 became $8.42, self-sealed
/// as valid.
/// </summary>
[Service]
public class InsiderTransactionPriceValidator
{
    /// <summary>
    /// Reject when the reported per-share price exceeds the close (on both
    /// bases) by more than this multiple. Real intraday spreads vs. close are
    /// well under 2×; common stocks above $100k/share don't exist outside
    /// BRK.A; 10× is a generous ceiling that still catches the actual failure
    /// mode (per-share field containing the total dollar value, which is
    /// Shares × close = thousands of times the unit price).
    /// </summary>
    public const decimal MaxPriceToCloseMultiplier = 10m;

    /// <summary>
    /// Slack around the session range when accepting a repaired price: the
    /// candidate must land inside [Low × 0.9, High × 1.1] on one of the two
    /// bases. A genuine fat-finger's total ÷ shares reproduces a real fill
    /// inside the day's range; a coincidence does not.
    /// </summary>
    public const decimal RepairBandLowFactor = 0.9m;
    public const decimal RepairBandHighFactor = 1.1m;

    /// <summary>
    /// Fallback repair band when the bar carries no usable Low/High: half to
    /// double the close, on either basis. Wider than the range band because a
    /// close alone says nothing about the day's extremes.
    /// </summary>
    public const decimal RepairFallbackLowFactor = 0.5m;
    public const decimal RepairFallbackHighFactor = 2m;

    /// <summary>
    /// Quick single-basis plausibility probe against a close. Basis-naive by
    /// design (no split context) — kept for spot checks; the persisting paths
    /// all go through <see cref="Evaluate"/>, which checks both bases.
    /// </summary>
    public bool IsPlausible(decimal pricePerShare, string securityTitle, decimal? close)
    {
        // Holdings (Form 3 sentinels) and post-transaction-only rows report
        // 0 price by design — not a real per-share price to validate.
        if (pricePerShare == 0m)
            return true;

        // Negative is nonsense but not what we're hunting; leave alone.
        if (pricePerShare < 0m)
            return true;

        // Derivative rows carry the derivative instrument's own price, which
        // can legitimately diverge from the underlying close (e.g. an option
        // strike or a deeply OTM warrant). The dashboard sort weighs them
        // equally so they can produce surprising values, but the validation
        // rule (10× close) doesn't apply.
        if (InsiderSecurityClassification.IsDerivativeTitle(securityTitle))
            return true;

        // No close on file (delisted, brand-new IPO, foreign listing not in
        // the Yahoo feed). Can't validate — don't penalize.
        if (!close.HasValue || close.Value <= 0m)
            return true;

        // Compare via division rather than close * multiplier: an extreme-but-
        // legal close (near decimal.MaxValue) overflows the product, and this
        // method must always return a verdict, never throw.
        return pricePerShare / MaxPriceToCloseMultiplier <= close.Value;
    }

    /// <summary>
    /// Full tri-state evaluation of a reported per-share price, plus the repair.
    /// Pure — the caller supplies the daily bar + split basis and persists the
    /// outcome.
    ///
    /// Differs from <see cref="IsPlausible"/> in three ways:
    /// <list type="bullet">
    /// <item>A missing close — or an ambiguous split basis — yields a
    /// <em>pending</em> result (null) instead of valid, so the row is
    /// re-checked once the close lands or the basis settles rather than being
    /// silently accepted (or worse, silently repaired).</item>
    /// <item>Plausibility runs on both bases: the stored close as-is, and the
    /// close restated onto the as-filed basis via the split factor. A pre-split
    /// price is VALID AS FILED, never a repair candidate.</item>
    /// <item>An implausible price is repaired (total ÷ shares) only when the
    /// candidate lands inside the session's price band on one of the two bases
    /// — a repair must reproduce a price the market actually traded at, never
    /// fabricate one. Outside the band the row is flagged invalid, unrepaired.</item>
    /// </list>
    ///
    /// Derivative classification uses the authoritative <paramref name="kind"/>
    /// (from the Form 4 table). Only when it's <see cref="InsiderSecurityKind.Unknown"/>
    /// (rows not yet reclassified) does it fall back to the title-keyword heuristic.
    ///
    /// <paramref name="notes"/> are the row's resolved footnotes. When they show
    /// an ADS/ADR unit mismatch (an ordinary-share count priced per ADS), the
    /// per-ADS price is first restated to per-ordinary via the ADS ratio so the
    /// downstream Shares × EffectivePrice is a real value — see
    /// <see cref="AdsRatioExtractor"/>. <paramref name="reportedPrice"/> stays the
    /// as-filed (per-ADS) figure that the caller keeps in ReportedPricePerShare.
    /// </summary>
    public InsiderTransactionPriceEvaluation Evaluate(
        decimal reportedPrice,
        long shares,
        InsiderSecurityKind kind,
        string securityTitle,
        DailyBarContext bar,
        IReadOnlyList<string> notes = null
    )
    {
        // Zero/negative prices (holdings, sentinels) and derivatives need no
        // close — they're valid as-is and never repaired.
        if (reportedPrice <= 0m || IsDerivative(kind, securityTitle))
        {
            return new InsiderTransactionPriceEvaluation
            {
                IsPriceValid = true,
                EffectivePrice = reportedPrice,
            };
        }

        // ADS/ADR unit normalization: when the footnotes show the price is per
        // ADS but the share count is the underlying ordinary count, restate the
        // price to per-ordinary so it matches the count. Everything below works
        // on this base price; the as-filed per-ADS value is preserved by the
        // caller in ReportedPricePerShare.
        var basePrice = reportedPrice;
        if (
            AdsRatioExtractor.TryGetOrdinarySharesPerAds(
                securityTitle,
                notes,
                shares,
                out var ratio
            )
        )
            basePrice = reportedPrice / ratio;

        // A real price we can't yet check stays pending (null), not valid —
        // and a series whose split basis is unsettled is exactly as
        // uncheckable as a missing close: any verdict would be a guess off by
        // the split ratio.
        var close = bar?.Close;
        if (!close.HasValue || close.Value <= 0m || bar.SplitBasisAmbiguous)
        {
            return new InsiderTransactionPriceEvaluation
            {
                IsPriceValid = null,
                EffectivePrice = basePrice,
            };
        }

        // Plausible against the close on EITHER basis — keep as filed. The
        // factor-scaled comparison is what accepts a correct pre-split price
        // (AMZN $3,300.24 vs the adjusted $165 close, factor 20). Divide
        // rather than multiply the price side so a near-decimal.MaxValue
        // close can't overflow.
        var factor = bar.SplitFactorToPresent > 0m ? bar.SplitFactorToPresent : 1m;
        var scaled = basePrice / MaxPriceToCloseMultiplier;
        if (scaled <= close.Value || scaled / factor <= close.Value)
        {
            return new InsiderTransactionPriceEvaluation
            {
                IsPriceValid = true,
                EffectivePrice = basePrice,
            };
        }

        // Implausible but unrepairable without a positive share count. A zero
        // count can't be divided; a negative one (a malformed/amended Form 4
        // carries "-N" through long.TryParse) would only "repair" into a
        // negative per-share price, which is never a real unit price — so it's
        // rejected the same way.
        if (shares <= 0)
        {
            return new InsiderTransactionPriceEvaluation
            {
                IsPriceValid = false,
                EffectivePrice = basePrice,
            };
        }

        // Repair candidate: the mis-entered total divided by the share count.
        // Accepted only when it reproduces a price inside the session's band
        // on one of the two bases — otherwise flag, never fabricate.
        var candidate = basePrice / shares;
        if (IsInsideRepairBand(candidate, bar, factor))
        {
            return new InsiderTransactionPriceEvaluation
            {
                IsPriceValid = true,
                EffectivePrice = candidate,
                WasRepaired = true,
            };
        }

        return new InsiderTransactionPriceEvaluation
        {
            IsPriceValid = false,
            EffectivePrice = basePrice,
        };
    }

    private static bool IsInsideRepairBand(decimal candidate, DailyBarContext bar, decimal factor)
    {
        decimal bandLow;
        decimal bandHigh;
        if (bar.Low is > 0m && bar.High >= bar.Low)
        {
            bandLow = bar.Low.Value * RepairBandLowFactor;
            bandHigh = bar.High.Value * RepairBandHighFactor;
        }
        else
        {
            bandLow = bar.Close.Value * RepairFallbackLowFactor;
            bandHigh = bar.Close.Value * RepairFallbackHighFactor;
        }

        // Present basis, then the as-filed basis (band × factor).
        if (candidate >= bandLow && candidate <= bandHigh)
            return true;
        return candidate >= bandLow * factor && candidate <= bandHigh * factor;
    }

    // Authoritative when the row carries a known kind (parsed from the Form 4
    // table); only Unknown rows (not yet reclassified) fall back to the title
    // keyword heuristic.
    private static bool IsDerivative(InsiderSecurityKind kind, string securityTitle)
    {
        return kind switch
        {
            InsiderSecurityKind.Derivative => true,
            InsiderSecurityKind.NonDerivative => false,
            _ => InsiderSecurityClassification.IsDerivativeTitle(securityTitle),
        };
    }
}
