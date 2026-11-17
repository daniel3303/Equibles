namespace Equibles.Holdings.Repositories.Models;

/// <summary>A holder's quarter whose 13F withholds every position under confidential treatment.</summary>
public sealed class WithheldBook
{
    public DateOnly ReportDate { get; init; }
    public Guid HolderId { get; init; }

    // Filled only by reads that name the holder.
    public string Name { get; init; }
    public string Cik { get; init; }
}
