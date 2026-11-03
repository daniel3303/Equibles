using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>
/// One filer's latest Schedule 13D or 13G on one listed security: the stake as last filed and
/// where it stands today. Event-dated, never a quarter-end portfolio.
/// </summary>
public sealed class HolderBeneficialOwnershipStake
{
    public Guid EquityIssuerId { get; init; }
    public string IssuerName { get; init; }

    /// <summary>The ticker the stake was filed on: the exact listed class or unit, else the issuer's presentation ticker.</summary>
    public string Ticker { get; init; }

    /// <summary>The issuer's presentation ticker today; null when the issuer has no presentation line.</summary>
    public string CurrentTicker { get; init; }

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

    /// <summary>Against the filer's previous 13D or 13G on the security; null without one.</summary>
    public long? ChangeShares { get; init; }

    public DateOnly? PriorEventDate { get; init; }

    /// <summary>True when a split after the event cannot be attributed, so counts stay as filed.</summary>
    public bool SplitUnresolved { get; init; }

    /// <summary>False for a legacy row whose cover page was never read.</summary>
    public bool HasCoverPage { get; init; }

    public Schedule13DGStakeStatus Status { get; init; }

    /// <summary>The day the issuer's presentation line stopped trading, when known.</summary>
    public DateOnly? DelistedOn { get; init; }

    /// <summary>The purpose a 13D states in Item 4; null for a 13G or without a reading.</summary>
    public StatedPurpose Purpose { get; set; }
}
