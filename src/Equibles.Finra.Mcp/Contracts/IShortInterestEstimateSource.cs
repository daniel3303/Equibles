using Equibles.CommonStocks.Data.Models;

namespace Equibles.Finra.Mcp.Contracts;

/// <summary>
/// Optional supplement to <c>GetShortInterest</c>'s official series: an estimate of the
/// settlement that has not been published yet. FINRA measures short interest twice a month and
/// disseminates each file five business days after the filing deadline, so the newest reported
/// row a caller can read is routinely two to three weeks stale — long enough for the position to
/// have moved materially.
///
/// A deployment that can estimate the pending settlement registers an implementation; nothing is
/// registered by default and none is required. The tool answers with the official FINRA series
/// alone whenever no implementation is present, so this seam changes no existing output.
/// </summary>
public interface IShortInterestEstimateSource
{
    /// <summary>
    /// A ready-to-append Markdown block describing the pending settlement's estimate, or
    /// <c>null</c> when there is nothing to add — the settlement was already published, or the
    /// implementation's own eligibility gates did not pass.
    /// </summary>
    /// <remarks>
    /// The returned block is appended below the official table, never merged into it: an estimate
    /// must stay visibly separate from FINRA's reported positions, and must label itself as an
    /// estimate. It is the implementation's job to say so — the tool appends the text verbatim.
    /// </remarks>
    Task<string> Describe(CommonStock stock, CancellationToken cancellationToken = default);
}
