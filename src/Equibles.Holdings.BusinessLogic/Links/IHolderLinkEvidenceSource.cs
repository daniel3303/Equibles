using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Links;

/// <summary>
/// Optional supplier of parent evidence the SEC filings cannot state, such as GLEIF accounting
/// parents. Nothing is registered by default, so the rollup rests on 13F evidence alone; a
/// deployment adds one with <c>TryAddEnumerable</c>.
/// </summary>
public interface IHolderLinkEvidenceSource
{
    /// <summary>
    /// Current <see cref="HolderLinkKind.GleifDirectParent"/> and
    /// <see cref="HolderLinkKind.GleifUltimateParent"/> rows, each with the child's CIK and the
    /// parent's CIK when its LEI maps to one. Rows of other kinds or without a valid child CIK are
    /// dropped; an exception fails the refresh, leaving the stored links as they were.
    /// </summary>
    Task<IReadOnlyList<HolderLinkEvidence>> Collect(
        DateOnly today,
        CancellationToken cancellationToken
    );
}
