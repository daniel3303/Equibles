using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.Data.Models;

[PrimaryKey(nameof(Dataset), nameof(PartitionDate), nameof(ScopeKey))]
[Index(nameof(Dataset), nameof(ScopeKey), nameof(PartitionDate))]
public class FinraImportPartition
{
    [MaxLength(64)]
    public string Dataset { get; set; }

    public DateOnly PartitionDate { get; set; }

    [MaxLength(80)]
    public string ScopeKey { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
