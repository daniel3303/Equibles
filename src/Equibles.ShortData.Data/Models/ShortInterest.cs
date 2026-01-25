using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.ShortData.Data.Models;

[Index(nameof(CommonStockId), nameof(SettlementDate), IsUnique = true)]
[Index(nameof(SettlementDate))]
public class ShortInterest {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly SettlementDate { get; set; }

    public long CurrentShortPosition { get; set; }
    public long PreviousShortPosition { get; set; }
    public long ChangeInShortPosition { get; set; }

    public long? AverageDailyVolume { get; set; }
    public decimal? DaysToCover { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
