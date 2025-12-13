using System.ComponentModel.DataAnnotations;

namespace Equibles.Media.Data.Models;

public class File {
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

    public virtual FileContent FileContent { get; set; } = new();
}