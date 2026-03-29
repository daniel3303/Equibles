using Microsoft.EntityFrameworkCore;

namespace Equibles.Cboe.Data.Models;

[Index(nameof(RatioType), nameof(Date), IsUnique = true)]
[Index(nameof(Date))]
public class CboePutCallRatio {
    public Guid Id { get; set; } = Guid.NewGuid();

    public CboePutCallRatioType RatioType { get; set; }

    public DateOnly Date { get; set; }

    public long? CallVolume { get; set; }
    public long? PutVolume { get; set; }
    public long? TotalVolume { get; set; }
    public decimal? PutCallRatio { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
