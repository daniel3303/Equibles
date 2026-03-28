using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.Data.Models;

[Index(nameof(CommonStockId), nameof(Date), IsUnique = true)]
[Index(nameof(Date))]
public class DailyShortVolume {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly Date { get; set; }

    public long ShortVolume { get; set; }
    public long ShortExemptVolume { get; set; }
    public long TotalVolume { get; set; }

    [MaxLength(16)]
    public string Market { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
