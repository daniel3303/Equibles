namespace Equibles.Media.HostedService.Configuration;

/// <summary>
/// Bound from the "FileBackfill" configuration section. Controls the one-off drain of
/// existing database blobs onto the filesystem store. Disabled by default.
/// </summary>
public class FileBackfillOptions
{
    /// <summary>Master switch for the backfill drain worker.</summary>
    public bool Enabled { get; set; }

    /// <summary>Rows claimed per tick.</summary>
    public int BatchSize { get; set; } = 100;
}
