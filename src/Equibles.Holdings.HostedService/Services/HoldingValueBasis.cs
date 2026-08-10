using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reconciles the two share bases a derived holding value multiplies together.
/// </summary>
/// <remarks>
/// <para>
/// A holding's <c>Value</c> is derived as shares × a stored period close. The share count is as
/// filed, while the stored close has no basis metadata: Yahoo full-history reconciliation can
/// serve either an adjusted or as-traded series. <see cref="SplitBasisResolver"/> therefore proves
/// only the captured split factor and pending-marker state, not the close's actual basis.
/// </para>
/// <para>
/// This legacy helper returns that captured split factor so the caller can fold it into the value
/// product without rounding the share count first. Applying it is not proof that the result matches
/// the stored close's basis and can double-apply a split when the provider served as-traded history;
/// the missing raw-price basis signal is tracked as a separate lane defect.
/// </para>
/// <para>
/// A pending captured split remains an explicit ambiguity: the caller leaves that row pending
/// until reconciliation stamps the split. A completed stamp closes only that metadata window; it
/// still does not identify the provider-served raw-price basis.
/// </para>
/// </remarks>
internal static class HoldingValueBasis
{
    /// <summary>
    /// Resolves the captured share-count split factor since <paramref name="reportDate"/>. Returns
    /// <c>false</c> when a relevant split is pending or cannot be attributed; this does not prove
    /// which basis the stored raw close uses. <paramref name="shareCountFactor"/> is then 1 and
    /// must not be used.
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
        // The walk itself lives in SplitBasisResolver (Equibles.CorporateActions.Data) so the
        // insider price lane restates on the identical rules; this wrapper keeps the holdings
        // vocabulary and remarks at the call sites.
        return SplitBasisResolver.TryResolveFactor(
            reportDate,
            splits,
            listedTicker,
            primaryTicker,
            secondaryTickers,
            out shareCountFactor
        );
    }
}
