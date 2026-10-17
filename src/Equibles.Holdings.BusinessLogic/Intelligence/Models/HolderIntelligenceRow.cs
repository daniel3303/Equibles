using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>One Form 13F holding row as the holder-intelligence engine reads it.</summary>
public sealed record HolderIntelligenceRow
{
    public Guid InstitutionalHolderId { get; init; }
    public Guid EquityIssuerId { get; init; }
    public string ListedTicker { get; init; }
    public DateOnly ReportDate { get; init; }
    public ShareType ShareType { get; init; }
    public OptionType? OptionType { get; init; }
    public long Shares { get; init; }
    public long Value { get; init; }

    /// <summary>True when the row's value is pending or unavailable, so it carries no weight.</summary>
    public bool ValueMissing { get; init; }

    public HolderSecurityKey Security => new(EquityIssuerId, ListedTicker);
}
