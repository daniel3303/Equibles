using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Links;

/// <summary>Reads holder parent links for consumers that group or nest holders.</summary>
public interface IHolderRollupReader
{
    /// <summary>
    /// The stored rollup of each requested CIK, keyed by canonical CIK; a CIK without a row rolls up
    /// to itself.
    /// </summary>
    Task<IReadOnlyDictionary<string, HolderRollup>> GetRollups(
        IEnumerable<string> ciks,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The 13F reporting evidence that puts a child's holdings inside this holder's report for the
    /// quarter: evidence is valid for its own quarter and the next quarter end. GLEIF ownership is
    /// never containment, and every row keeps its kind, since a cover listing is a partial report.
    /// </summary>
    Task<IReadOnlyList<HolderLinkEvidence>> GetContainedChildren(
        InstitutionalHolder holder,
        DateOnly quarter,
        CancellationToken cancellationToken = default
    );

    /// <summary>The 13F filer link of each requested fund series that has one, keyed by series ID.</summary>
    Task<IReadOnlyDictionary<string, FundSeriesHolderLink>> GetFundSeriesLinks(
        IEnumerable<string> seriesIds,
        CancellationToken cancellationToken = default
    );
}
