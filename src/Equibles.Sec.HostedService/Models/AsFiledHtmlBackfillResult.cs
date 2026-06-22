namespace Equibles.Sec.HostedService.Models;

/// <summary>Per-cycle tally of what the as-filed HTML backfill did, for logging.</summary>
public class AsFiledHtmlBackfillResult
{
    public int Processed { get; set; }
    public int Built { get; set; }
    public int NoExhibit { get; set; }
    public int Failed { get; set; }
}
