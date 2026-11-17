namespace Equibles.Holdings.Repositories.Models;

/// <summary>A holder's quarter whose 13F withholds every position under confidential treatment.</summary>
public sealed class WithheldBook
{
    public DateOnly ReportDate { get; init; }
    public Guid HolderId { get; init; }
}
