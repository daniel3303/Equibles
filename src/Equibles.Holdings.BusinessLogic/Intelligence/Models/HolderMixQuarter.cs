using Equibles.Holdings.BusinessLogic.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// A stock's holders in one quarter, split by every <see cref="HolderMixDimension"/>. Each
/// dimension's buckets sum to <see cref="Total"/>.
/// </summary>
public sealed class HolderMixQuarter
{
    public DateOnly ReportDate { get; init; }

    // True for the open quarter shown as the combined view: holders yet to file carry their prior
    // positions.
    public bool IsCombined { get; init; }

    // The measured quarter style, turnover, horizon and book size read: the newest on or before
    // ReportDate, null when none is.
    public DateOnly? MeasuredQuarter { get; init; }

    // The comparison with the quarter before; its Note names the filers left out of the change.
    public HoldingsComparisonStatus Comparison { get; init; }

    public HolderMixBucket Total { get; init; }

    public IReadOnlyDictionary<
        HolderMixDimension,
        IReadOnlyList<HolderMixBucket>
    > Dimensions { get; init; } =
        new Dictionary<HolderMixDimension, IReadOnlyList<HolderMixBucket>>();
}
