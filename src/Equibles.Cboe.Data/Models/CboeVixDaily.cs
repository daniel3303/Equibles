using Microsoft.EntityFrameworkCore;

namespace Equibles.Cboe.Data.Models;

[Index(nameof(Date), IsUnique = true)]
public class CboeVixDaily {
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly Date { get; set; }

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
