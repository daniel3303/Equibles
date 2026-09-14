using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>Immutable original records retained when historical correction backups are retired.</summary>
[Index(nameof(CorrectionKey), nameof(SourceTable), nameof(SourceRecordId))]
public class HoldingsCorrectionEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string CorrectionKey { get; set; }

    [Required, MaxLength(100)]
    public string SourceTable { get; set; }

    [MaxLength(100)]
    public string SourceRecordId { get; set; }

    /// <summary>Ordered source column names and PostgreSQL types, including precision.</summary>
    [Required, Column(TypeName = "jsonb")]
    public string SourceSchema { get; set; }

    /// <summary>Every original field, including unresolved identifiers and nulls.</summary>
    [Required, Column(TypeName = "jsonb")]
    public string OriginalRow { get; set; }

    /// <summary>Time of preservation, not the unknown time of the historical correction.</summary>
    public DateTime MigratedAt { get; set; }
}
