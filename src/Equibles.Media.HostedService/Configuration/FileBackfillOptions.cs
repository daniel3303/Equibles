namespace Equibles.Media.HostedService.Configuration;

/// <summary>
/// Bound from the "FileBackfill" configuration section. Controls the one-off drain of
/// existing database blobs onto the filesystem store. Disabled by default.
/// </summary>
public class FileBackfillOptions
{
    /// <summary>Master switch for the backfill drain worker.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Rows claimed per pass. Memory is bounded by <see cref="Concurrency"/> (blobs in
    /// flight), not by the batch size, so this mainly amortizes the claim query and the
    /// per-batch commit.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// How many blobs are loaded and written concurrently within a batch. Peak memory is
    /// roughly this many blobs (audio runs tens of MB each); it also sets the database
    /// read parallelism, so keep it well below the connection pool size.
    /// </summary>
    public int Concurrency { get; set; } = 8;
}
