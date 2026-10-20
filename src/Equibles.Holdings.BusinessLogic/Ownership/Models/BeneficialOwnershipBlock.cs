using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>A filer's latest Schedule 13D or 13G for a listing, with every reporting person it names.</summary>
public sealed class BeneficialOwnershipBlock
{
    public string FilerName { get; init; }
    public string FilerCik { get; init; }
    public FilingType FilingType { get; init; }
    public bool IsAmendment { get; init; }
    public DateOnly EventDate { get; init; }
    public DateOnly FilingDate { get; init; }
    public string AccessionNumber { get; init; }
    public Schedule13GRules Rules { get; init; }

    /// <summary>The lead person's shares, restated onto today's split basis.</summary>
    public long Shares { get; init; }

    public long FiledShares { get; init; }
    public decimal? PercentOfClass { get; init; }

    /// <summary>Against the filer's previous 13D or 13G for the listing; null without one.</summary>
    public long? ChangeShares { get; init; }

    public DateOnly? PriorEventDate { get; init; }

    /// <summary>True when a split after the event cannot be attributed, so counts stay as filed.</summary>
    public bool SplitUnresolved { get; init; }

    /// <summary>False for a legacy row whose cover page was never read, so no persons are known.</summary>
    public bool HasCoverPage { get; init; }

    public IReadOnlyList<BeneficialOwnershipPerson> Persons { get; init; } = [];
}
