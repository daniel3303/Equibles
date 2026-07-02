namespace Equibles.Media.HostedService.Configuration;

/// <summary>
/// Bound from the "BlobSweep" configuration section. Controls the deletion sweep that
/// retires filesystem blobs whose File rows are gone. Disabled by default.
/// </summary>
public class BlobSweepOptions
{
    /// <summary>Master switch for the deletion sweep worker.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How old a queued deletion (and a blob's last write) must be before the sweep will
    /// touch it. Protects blobs whose rows are still inside an uncommitted write window.
    /// </summary>
    public int GraceHours { get; set; } = 48;

    /// <summary>
    /// How long a trashed blob is retained before permanent deletion. The purge re-checks
    /// references one final time and restores the blob if a row reappeared — the window
    /// that makes retiring a shared, content-addressed blob reversible.
    /// </summary>
    public int TrashRetentionHours { get; set; } = 24;

    /// <summary>
    /// Enables the rolling disk-vs-database reconciliation that catches deletions the
    /// queue cannot see (database-level cascades, direct repository deletes). Each daily
    /// run covers one seventh of the shard space, so the whole store is reconciled weekly.
    /// </summary>
    public bool ReconciliationEnabled { get; set; } = true;
}
