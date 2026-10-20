namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// One pass of the 13D/13G coverage walk. <see cref="CompletedThrough"/> is the last daily index
/// fully read (null when none was), and <see cref="Stopped"/> is true when EDGAR became unavailable
/// before the requested range ended.
/// </summary>
public record Schedule13DGBackfillResult(
    DateOnly? CompletedThrough,
    int Documents,
    int Imported,
    bool Stopped
);
