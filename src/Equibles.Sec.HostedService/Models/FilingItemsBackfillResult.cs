namespace Equibles.Sec.HostedService.Models;

/// <summary>Per-cycle tally of what the filing-items backfill did, for logging.</summary>
public class FilingItemsBackfillResult
{
    /// <summary>
    /// Companies drawn from the pending set this cycle, whether or not their fetch then
    /// succeeded. Zero is the "nothing eligible" signal the worker idles on — distinct
    /// from "everything selected failed" (an EDGAR outage), where a backlog is still
    /// pending and the fast cadence must resume.
    /// </summary>
    public int Selected { get; set; }

    /// <summary>Companies whose submissions feed was fetched and whose 8-Ks were stamped.</summary>
    public int Companies { get; set; }

    /// <summary>Documents stamped with a non-empty item list from the feed.</summary>
    public int Stamped { get; set; }

    /// <summary>
    /// Documents checked but absent from the feed (or carrying no items there) — marked
    /// with the empty-string terminal value so they are not re-selected.
    /// </summary>
    public int NotFound { get; set; }

    /// <summary>Companies whose fetch or save failed; their documents retry next cycle.</summary>
    public int Failed { get; set; }
}
