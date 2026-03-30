using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

[Table("TranscriptCheckStatuses")]
[Index(nameof(CommonStockId), IsUnique = true)]
public class TranscriptCheckStatus {
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateTime LastCheckedAt { get; set; }

    public bool HasTranscripts { get; set; }
}
