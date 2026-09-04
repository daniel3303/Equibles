using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.Data.Models;

[Index(nameof(CommonStockId), nameof(ListedTicker), nameof(Date), IsUnique = true)]
[Index(nameof(Date))]
public class DailyShortVolume
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [Required]
    [MaxLength(TickerNormalizer.MaxListedLength)]
    public string ListedTicker { get; set; } = "";

    public DateOnly Date { get; set; }

    [Precision(28, 6)]
    public decimal ShortVolume { get; set; }

    [Precision(28, 6)]
    public decimal ShortExemptVolume { get; set; }

    [Precision(28, 6)]
    public decimal TotalVolume { get; set; }

    [MaxLength(16)]
    public string Market { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
