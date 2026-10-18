namespace Equibles.Holdings.BusinessLogic.Models;

// One stored succession with both filers' names and its group's 13F book values.
public sealed record HoldingsSuccessionPair
{
    public Guid PredecessorHolderId { get; init; }
    public string PredecessorName { get; init; }
    public Guid SuccessorHolderId { get; init; }
    public string SuccessorName { get; init; }
    public DateOnly SuccessorFirstReportDate { get; init; }
    public long PredecessorGroupValue { get; init; }
    public long SuccessorGroupValue { get; init; }
}
