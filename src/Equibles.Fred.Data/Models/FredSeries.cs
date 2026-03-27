using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Data.Models;

[Index(nameof(SeriesId), IsUnique = true)]
[Index(nameof(Category))]
public class FredSeries {
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(64)]
    public string SeriesId { get; set; }

    [Required]
    [MaxLength(256)]
    public string Title { get; set; }

    public FredSeriesCategory Category { get; set; }

    [MaxLength(32)]
    public string Frequency { get; set; }

    [MaxLength(128)]
    public string Units { get; set; }

    [MaxLength(64)]
    public string SeasonalAdjustment { get; set; }

    public DateOnly? ObservationStart { get; set; }
    public DateOnly? ObservationEnd { get; set; }

    public DateTime? LastUpdated { get; set; }

    public virtual ICollection<FredObservation> Observations { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
