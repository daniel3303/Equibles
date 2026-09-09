namespace Equibles.Holdings.BusinessLogic.Models;

public sealed record HoldingsFilingPresence
{
    public DateOnly ReportDate { get; init; }
    public Guid InstitutionalHolderId { get; init; }
}
