using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Equibles.Sec.Data.Models.Chunks;

[Index(nameof(ChunkId), nameof(Model), IsUnique = true)]
public class Embedding {
    public Guid Id { get; set; } = Guid.NewGuid();

    public virtual Chunk Chunk { get; set; }
    public Guid ChunkId { get; set; }


    /// <summary>
    /// The model used to generate the embedding.
    /// </summary>
    [MaxLength(64)]
    public string Model { get; set; }

    public Vector Vector { get; set; }

    public int VectorDimension { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

}