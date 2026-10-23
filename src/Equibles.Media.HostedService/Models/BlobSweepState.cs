namespace Equibles.Media.HostedService.Models;

/// <summary>The sweep's saved progress: its last completed run and the run in flight, if any.</summary>
public class BlobSweepState
{
    public DateTime? LastCompletedAt { get; set; }

    /// <summary>The reconciliation slot of the last completed run; the next run takes the following one.</summary>
    public int? LastSlot { get; set; }

    public BlobSweepRun Run { get; set; }
}
