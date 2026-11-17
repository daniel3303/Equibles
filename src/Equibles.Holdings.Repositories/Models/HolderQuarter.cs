namespace Equibles.Holdings.Repositories.Models;

/// <summary>One holder's reported quarter.</summary>
public sealed class HolderQuarter
{
    public Guid HolderId { get; init; }
    public DateOnly ReportDate { get; init; }
}
