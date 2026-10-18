namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// One filer's 13F rows of a stock in one quarter, share counts restated to today's split basis.
/// Principal amounts are left out; option rows count notional underlying shares.
/// </summary>
public sealed record HolderMixPosition
{
    public DateOnly ReportDate { get; init; }
    public Guid InstitutionalHolderId { get; init; }
    public long Shares { get; init; }
    public long Value { get; init; }
    public long CallShares { get; init; }
    public long CallValue { get; init; }
    public long PutShares { get; init; }
    public long PutValue { get; init; }
}
