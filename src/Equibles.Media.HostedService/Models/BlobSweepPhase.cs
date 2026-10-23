namespace Equibles.Media.HostedService.Models;

/// <summary>The sweep's resumable walks in run order; a saved run resumes at its phase.</summary>
public enum BlobSweepPhase
{
    Purge,
    Reconcile,
}
