using System.ComponentModel.DataAnnotations;

namespace Equibles.Media.Data.Models;

public class File
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [StringLength(256)]
    public string Name { get; set; }
    public string NameWithExtension => Name + "." + Extension;

    [StringLength(16)]
    public string Extension { get; set; } // file extension without the period '.' eg. png

    [StringLength(64)]
    public string ContentType { get; set; } // eg. text/html
    public long Size { get; set; } // Size in bytes

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    // Where the bytes physically live. Defaults to Database so existing rows and callers
    // are unchanged; FileSystem-stored files carry RelativePath + ContentHash instead of
    // a FileContent row. Indexed so the migration drain worker can cheaply find un-migrated rows.
    public StorageProvider StorageProvider { get; set; } = StorageProvider.Database;

    // Relative path within the configured filesystem store root, e.g.
    // "blob/sha256/a7/f3/a7f3…". Set only when StorageProvider == FileSystem. Always
    // relative so the store can move between disks without touching rows.
    [StringLength(128)]
    public string RelativePath { get; set; }

    // Algorithm-prefixed content hash, e.g. "sha256:a7f3…". Set on filesystem writes;
    // enables byte-level dedup and integrity checks.
    [StringLength(80)]
    public string ContentHash { get; set; }

    // Present only for Database-stored files.
    public virtual FileContent FileContent { get; set; }
}
