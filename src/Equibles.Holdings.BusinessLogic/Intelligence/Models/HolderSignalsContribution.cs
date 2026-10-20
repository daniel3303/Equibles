using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>What one filer adds to the quarter's stock signals.</summary>
public sealed record HolderSignalsContribution
{
    public Guid InstitutionalHolderId { get; init; }
    public HolderQuarterBook Book { get; init; }

    /// <summary>The filer's valued common-share book, the denominator of its weights.</summary>
    public long EquityAum { get; init; }

    public HolderStyleBand StyleBand { get; init; }
    public HolderTurnoverBand TurnoverBand { get; init; }

    /// <summary>The filer advises private funds by its current Form ADV.</summary>
    public bool AdvisesPrivateFunds { get; init; }

    /// <summary>The filer is in the top fifth by current three-year alpha.</summary>
    public bool IsTopScored { get; init; }
}
