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
/// close was 12.5M×).</item>
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
    /// Whether a derived value disagrees with the filed one grossly enough that the filed figure
    /// must be published instead. Only meaningful when the filer reported a positive value.
    /// </summary>
    internal static bool GrosslyExceedsFiled(decimal derivedValue, long? filedValue) =>
        filedValue is > 0 && derivedValue > DisagreementCapMultiple * filedValue.Value;
}
