using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Cftc.Data.Models;

[Index(nameof(MarketCode), IsUnique = true)]
[Index(nameof(Category))]
public class CftcContract {
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(16)]
    public string MarketCode { get; set; }

    [Required]
    [MaxLength(256)]
    public string MarketName { get; set; }

    public CftcContractCategory Category { get; set; }

    public DateOnly? LatestReportDate { get; set; }

    public DateTime? LastUpdated { get; set; }

    public virtual ICollection<CftcPositionReport> Reports { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
