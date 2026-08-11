namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Plausibility gate for a derived holding value, applied wherever shares × close is computed.
/// </summary>
/// <remarks>
/// <para>
/// The derivation multiplies a filer-controlled share count by whatever the price lane stored, so a
/// single corrupt close corrupts every position in that stock: one series carrying eight-figure
/// per-share closes inflated 1,804 positions across 263 institutions by a combined $102.8T, and one
/// of them rendered as 76.7% of the largest filer's portfolio. <see cref="ValueBasisAudit"/> already
/// measured such disagreements but only logged them; this class is the acting version of the same
/// signature.
/// </para>
/// <para>
/// Two rules, in order of preference:
/// <list type="number">
/// <item><b>Relative</b> — when the filer reported a value, a derivation more than
/// <see cref="DisagreementCapMultiple"/>× above it is basis-suspect and the filed figure is
/// published instead. The cap sits far above <see cref="ValueBasisAudit.DisagreementMultiple"/>
/// (ordinary mark drift) and far below any real units error (a missed split is ≥2×, the corrupt
/// close was 12.5M×). One carve-out: a disagreement clustered at ~1,000× is the FILED figure's
/// units, not the derivation's — see <see cref="FiledLooksThousandsScaled"/>.</item>
/// <item><b>Absolute</b> — with no filed value to compare against, a close above
/// <see cref="MaxPlausibleSharePrice"/> is refused outright and the row stays pending. No US
/// equity trades near $1M/share (BRK-A is ~$800k); the threshold must never be tightened below
/// that.</item>
/// </list>
/// </para>
/// </remarks>
internal static class HoldingValueSanityGuard
{
    /// <summary>
    /// Ceiling on a per-share close used for valuation. BRK-A (~$800k) must stay below it.
    /// </summary>
    internal const decimal MaxPlausibleSharePrice = 1_000_000m;

    /// <summary>
    /// A derivation more than this multiple above the filer's own value is treated as a basis
    /// error rather than published.
    /// </summary>
    internal const decimal DisagreementCapMultiple = 5m;

    /// <summary>A close this large cannot honestly price anything — refuse to derive.</summary>
    internal static bool IsImplausibleClose(decimal closePrice) =>
        closePrice > MaxPlausibleSharePrice;

    /// <summary>
    /// Lower bound of the derived/filed ratio band read as "the filer reported thousands".
    /// </summary>
    internal const decimal ThousandsScaleMinMultiple = 900m;

    /// <summary>
    /// Upper bound of the derived/filed ratio band read as "the filer reported thousands".
    /// </summary>
    internal const decimal ThousandsScaleMaxMultiple = 1100m;

    /// <summary>
    /// Whether a derived value disagrees with the filed one grossly enough that the filed figure
    /// must be published instead. Only meaningful when the filer reported a positive value.
    /// </summary>
    internal static bool GrosslyExceedsFiled(decimal derivedValue, long? filedValue) =>
        filedValue is > 0 && derivedValue > DisagreementCapMultiple * filedValue.Value;

    /// <summary>
    /// Whether the filed figure itself is on a thousands basis: some filers still report the
    /// VALUE column in thousands after the SEC's 2023 whole-dollar switch (Baupost filed a ~$5B
    /// book that served as ~$5M this way), so the derivation lands at ~1,000× the filed figure.
    /// The band is deliberately tight (900×–1,100×): a derivation-side basis error CAN reach
    /// this magnitude — a duplicated share-count column inflates by exactly the share price
    /// (Corrupt13FShareCountRepairer), and depositary ratios reach 3,200×
    /// (ImpossiblePositionGuard's NaaS case) — so only the unit signature itself (≈1,000×,
    /// modulo mark drift) qualifies. The duplicated-column case is excluded structurally too:
    /// there the shares column IS the value column, so <paramref name="shares"/> equals the
    /// filed figure and the ratio is the share price, not a unit — a $900–$1,100 stock would
    /// otherwise land inside the band. Residual, accepted: rows this rule cannot see (a
    /// thousands filer whose price never arrives, published via the ladder-exhaust or
    /// stuck-zero filed fallbacks) still serve the thousands-scale figure — there is no scale
    /// signal without a derivation to compare against. Decision-site publishes are NOT part of
    /// that residual: their price can arrive (or change) after the decision, so
    /// <see cref="HoldingValueFallbackRepairService"/> re-examines them against current prices
    /// and resets the ones this band identifies.
    /// </summary>
    internal static bool FiledLooksThousandsScaled(
        decimal derivedValue,
        long shares,
        long? filedValue
    ) =>
        filedValue is > 0
        && shares != filedValue.Value
        && derivedValue >= ThousandsScaleMinMultiple * filedValue.Value
        && derivedValue <= ThousandsScaleMaxMultiple * filedValue.Value;

    /// <summary>
    /// The one decision both valuation sites act on: publish the filed figure only when the
    /// derivation grossly exceeds it AND the disagreement is not the filed figure's own
    /// thousands basis. The import inline derivation and the repricing lane must share this
    /// test, or the two publish different figures for the same row.
    /// </summary>
    internal static bool ShouldPublishFiledInsteadOfDerived(
        decimal derivedValue,
        long shares,
        long? filedValue
    ) =>
        GrosslyExceedsFiled(derivedValue, filedValue)
        && !FiledLooksThousandsScaled(derivedValue, shares, filedValue);
}
