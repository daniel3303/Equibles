using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

[Table("TranscriptCheckStatuses")]
[Index(nameof(EquityIssuerId), IsUnique = true)]
public class TranscriptCheckStatus
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // Retain the deployed column name until every older binary has retired.
    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }

    public DateTime LastCheckedAt { get; set; }

    public bool HasTranscripts { get; set; }
}
