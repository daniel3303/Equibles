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

    /// <summary>
    /// Full tri-state evaluation of a reported per-share price, plus the repair.
    /// Pure — the caller supplies the close and persists the outcome.
    ///
    /// Differs from <see cref="IsPlausible"/> in two ways:
    /// <list type="bullet">
    /// <item>A missing close yields a <em>pending</em> result (null) instead of
    /// valid, so the row is re-checked by a later recompute once the close
    /// lands rather than being silently accepted.</item>
    /// <item>An implausible real price is <em>repaired</em>: the per-share
    /// field almost always holds the total transaction value, so dividing by
    /// <paramref name="shares"/> recovers the unit price. Rows with no share
    /// count can't be divided and stay flagged invalid.</item>
    /// </list>
    ///
    /// Derivative classification uses the authoritative <paramref name="kind"/>
    /// (from the Form 4 table). Only when it's <see cref="InsiderSecurityKind.Unknown"/>
    /// (rows not yet reclassified) does it fall back to the title-keyword heuristic.
    /// </summary>
    public InsiderTransactionPriceEvaluation Evaluate(
        decimal reportedPrice,
        long shares,
        InsiderSecurityKind kind,
        string securityTitle,
        decimal? unadjustedClose
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

        // A real price we can't yet check stays pending (null), not valid.
        if (!unadjustedClose.HasValue || unadjustedClose.Value <= 0m)
        {
            return new InsiderTransactionPriceEvaluation
            {
                IsPriceValid = null,
                EffectivePrice = reportedPrice,
            };
        }

        // Plausible against the close — keep as filed.
        if (reportedPrice <= unadjustedClose.Value * MaxPriceToCloseMultiplier)
        {
            return new InsiderTransactionPriceEvaluation
            {
                IsPriceValid = true,
                EffectivePrice = reportedPrice,
            };
        }

        // Implausible but unrepairable without a share count.
        if (shares == 0)
        {
            return new InsiderTransactionPriceEvaluation
            {
                IsPriceValid = false,
                EffectivePrice = reportedPrice,
            };
        }

        // Implausible — repair by dividing the mis-entered total by the share
        // count and accept the result as the per-share price.
        return new InsiderTransactionPriceEvaluation
        {
            IsPriceValid = true,
            EffectivePrice = reportedPrice / shares,
            WasRepaired = true,
        };
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
            _ => IsDerivativeSecurity(securityTitle),
        };
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
