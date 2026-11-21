namespace Equibles.Sec.FinancialFacts.HostedService.Configuration;

/// <summary>
/// Options for the earnings-release statement lane. The lane is local (stored release text and
/// facts, no EDGAR), so these bound how far back it looks and how much one cycle holds.
/// </summary>
public class ReleaseStatementsOptions
{
    /// <summary>Minutes between sweeps for releases not read yet.</summary>
    public int SleepIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Days of releases the lane reads. A release older than this has its periodic report on
    /// file, or comes from a company that stopped filing them, so its figures are never served.
    /// </summary>
    public int LookbackDays { get; set; } = 120;

    /// <summary>Releases read per drain iteration.</summary>
    public int BatchSize { get; set; } = 100;
}
