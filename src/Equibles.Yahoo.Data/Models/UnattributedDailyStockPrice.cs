using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Data.Models;

// A retained issuer-level observation whose original listing was not recorded.
[Index(nameof(EquityIssuerId), nameof(Date), IsUnique = true)]
[Index(nameof(Date))]
public class UnattributedDailyStockPrice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }

    public DateOnly Date { get; set; }

    [Precision(18, 4)]
    public decimal Open { get; set; }

    [Precision(18, 4)]
    public decimal High { get; set; }

    [Precision(18, 4)]
    public decimal Low { get; set; }

    [Precision(18, 4)]
    public decimal Close { get; set; }

    [Precision(18, 4)]
    public decimal AdjustedClose { get; set; }

    public long Volume { get; set; }
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
