namespace Equibles.Media.HostedService.Models;

/// <summary>The sweep's phases in run order; a saved run resumes at its phase.</summary>
public enum BlobSweepPhase
{
    Queue,
    Purge,
    Reconcile,
}
