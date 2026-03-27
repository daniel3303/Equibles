using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Data.Models;

[Index(nameof(FredSeriesId), nameof(Date), IsUnique = true)]
[Index(nameof(Date))]
public class FredObservation {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FredSeriesId { get; set; }
    public virtual FredSeries FredSeries { get; set; }

    public DateOnly Date { get; set; }

    public decimal? Value { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
