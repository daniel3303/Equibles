using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Index(nameof(CompletedAt), nameof(RequestedAt))]
public class HoldingsCusipRescan
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquityIssuerId { get; set; }

    [MaxLength(32)]
    public string Ticker { get; set; }

    [MaxLength(32)]
    public string PreviousCusip { get; set; }

    [MaxLength(32)]
    public string Cusip { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateOnly? ScannedThrough { get; set; }
    public DateTime? CompletedAt { get; set; }
}
