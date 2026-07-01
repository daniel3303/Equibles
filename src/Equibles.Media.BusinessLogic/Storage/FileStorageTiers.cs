namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// Top-level partition of the filesystem store, chosen for durability placement — a
/// different disk/dataset can be mounted at <c>&lt;root&gt;/audio</c> so precious audio
/// gets its own redundancy — NOT for sharding. It never affects deduplication because
/// bytes of different kinds never collide. Reads use <c>File.RelativePath</c> directly,
/// so the tier only matters at write time.
/// </summary>
public static class FileStorageTiers
{
    /// <summary>Re-scrapable bulk content: filings, XBRL envelopes, images, PDFs, text.</summary>
    public const string Blob = "blob";

    /// <summary>Precious, hard-to-recapture content: earnings-call audio.</summary>
    public const string Audio = "audio";
}
