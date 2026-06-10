using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Data.Models;

// A scheduled or realized publication date of a FRED release. Future dates come from
// the FRED release calendar (include_release_dates_with_no_data=true).
[Index(nameof(FredReleaseId), nameof(Date), IsUnique = true)]
[Index(nameof(Date))]
public class FredReleaseDate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FredReleaseId { get; set; }
    public virtual FredRelease FredRelease { get; set; }

    public DateOnly Date { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
