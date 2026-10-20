using Equibles.Holdings.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Advisers;

/// <summary>
/// An adviser's Form ADV facts as one roster listed them: the roster date, the first roster with
/// this content, regulatory assets, client base and private funds.
/// </summary>
public sealed record AdviserFacts
{
    public int Crd { get; init; }
    public FormAdvRegistration Registration { get; init; }

    /// <summary>The roster the facts are read from.</summary>
    public DateOnly RosterDate { get; init; }

    /// <summary>The first roster that listed this content.</summary>
    public DateOnly Since { get; init; }

    /// <summary>The last roster that listed this content.</summary>
    public DateOnly Until { get; init; }

    public DateOnly? LatestFilingDate { get; init; }
    public long? RegulatoryAum { get; init; }
    public HolderClientBase ClientBase { get; init; }
    public decimal? ClientBaseAumShare { get; init; }
    public decimal? PooledVehicleAumShare { get; init; }
    public bool? AdvisesPrivateFunds { get; init; }
    public int? PrivateFundCount { get; init; }
    public long? PrivateFundGrossAssets { get; init; }
    public PrivateFundFocus PrivateFundFocus { get; init; }
    public PrivateFundFocusBasis? PrivateFundFocusBasis { get; init; }
    public DateOnly? PrivateFundFocusAsOf { get; init; }
}
