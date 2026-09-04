using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

[Index(nameof(CommonStockId), nameof(ListedTicker), nameof(SettlementDate), IsUnique = true)]
[Index(nameof(SettlementDate))]
public class FailToDeliver
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [Required]
    [MaxLength(TickerNormalizer.MaxListedLength)]
    public string ListedTicker { get; set; } = "";

    public DateOnly SettlementDate { get; set; }

    public long Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
