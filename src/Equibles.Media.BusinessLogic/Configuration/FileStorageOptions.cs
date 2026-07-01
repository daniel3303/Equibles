namespace Equibles.Media.BusinessLogic.Configuration;

/// <summary>
/// Bound from the "FileStorage" configuration section. When <see cref="Enabled"/> is
/// false (the default), everything behaves exactly as before — bytes stay in the
/// database — so the filesystem store is opt-in per deployment.
/// </summary>
public class FileStorageOptions
{
    /// <summary>Master switch. When false, FileSystem write requests fall back to the database.</summary>
    public bool Enabled { get; set; }

    /// <summary>Absolute physical root of the store, e.g. "/data/media". Required when <see cref="Enabled"/>.</summary>
    public string RootPath { get; set; }
}
