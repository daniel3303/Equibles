using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// Immutable source evidence, independent of the current normalized issuer/security/listing facts.
[Index(nameof(Source), nameof(SourceRecordKey), nameof(PayloadHash), IsUnique = true)]
public class EquityDirectorySourceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(64)]
    public string Source { get; set; }

    [Required, MaxLength(256)]
    public string SourceRecordKey { get; set; }

    [Required, MaxLength(64)]
    public string PayloadHash { get; set; }

    [Required, Column(TypeName = "jsonb")]
    public string PayloadJson { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
