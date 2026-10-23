namespace Equibles.Media.HostedService.Models;

/// <summary>An unfinished sweep run, saved so a restarted worker continues it.</summary>
public class BlobSweepRun
{
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// The reconciliation slice (shard byte modulo 7), fixed when the run starts so a run
    /// resumed on a later day still finishes the slice it began.
    /// </summary>
    public int Slot { get; set; }

    public BlobSweepPhase Phase { get; set; }

    /// <summary>
    /// The last folder the current phase finished, '/'-separated and relative to the phase's
    /// walk root; null until the phase finishes its first folder.
    /// </summary>
    public string Position { get; set; }
}
