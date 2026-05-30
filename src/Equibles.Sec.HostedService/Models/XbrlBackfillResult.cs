namespace Equibles.Sec.HostedService.Models;

/// <summary>Per-cycle tally of what the XBRL backfill did, for logging.</summary>
public class XbrlBackfillResult
{
    public int Processed { get; set; }
    public int Captured { get; set; }
    public int NotPresent { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
