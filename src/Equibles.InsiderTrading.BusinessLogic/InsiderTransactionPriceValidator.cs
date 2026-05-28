using Equibles.Core.AutoWiring;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Decides whether an InsiderTransaction's reported per-share price is
/// plausible. Catches the recurring filer-error class where a Form 4 filer
/// types the total transaction value into <c>transactionPricePerShare</c>
/// (a per-share field), which then explodes the dashboard's Shares × Price
/// sort into nonsense numbers (trillions, quadrillions).
///
/// Stateless and pure — the close price is passed in by the caller. Lookup
/// is done by callers that have repository access (parser at ingest time;
/// backfill manager during one-off recomputes).
/// </summary>
[Service]
public class InsiderTransactionPriceValidator
{
    /// <summary>
    /// Reject when the reported per-share price exceeds the unadjusted close
    /// by more than this multiple. Real intraday spreads vs. close are well
    /// under 2×; common stocks above $100k/share don't exist outside BRK.A;
    /// 10× is a generous ceiling that still catches the actual failure mode
    /// (per-share field containing the total dollar value, which is
    /// Shares × close = thousands of times the unit price).
    /// </summary>
    public const decimal MaxPriceToCloseMultiplier = 10m;

    private static readonly string[] DerivativeTitleKeywords =
    {
        "option",
        "warrant",
        "right to buy",
        "right to sell",
        "convertible",
    };

    public bool IsPlausible(decimal pricePerShare, string securityTitle, decimal? unadjustedClose)
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
        if (IsDerivativeSecurity(securityTitle))
            return true;

        // No close on file (delisted, brand-new IPO, foreign listing not in
        // the Yahoo feed). Can't validate — don't penalize.
        if (!unadjustedClose.HasValue || unadjustedClose.Value <= 0m)
            return true;

        return pricePerShare <= unadjustedClose.Value * MaxPriceToCloseMultiplier;
    }

    private static bool IsDerivativeSecurity(string securityTitle)
    {
        if (string.IsNullOrWhiteSpace(securityTitle))
            return false;
        return DerivativeTitleKeywords.Any(keyword =>
            securityTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        );
    }
}
