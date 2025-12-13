namespace Equibles.Media.Data.Models;

public class FileContent {
    public Guid Id { get; set; } = Guid.NewGuid();
    public byte[] Bytes { get; set; } = [];

    public virtual File File { get; set; }
    public Guid FileId { get; set; }
}