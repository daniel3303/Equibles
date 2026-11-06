using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Data.Models;

// A scheduled or realized publication date of a release. FRED's future dates come from its
// release calendar (include_release_dates_with_no_data=true); other publishers' from their schedules.
[Index(nameof(FredReleaseId), nameof(Date), IsUnique = true)]
[Index(nameof(Date))]
public class FredReleaseDate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FredReleaseId { get; set; }
    public virtual FredRelease FredRelease { get; set; }

    public DateOnly Date { get; set; }

    // The period the release covers, as the publisher states it; FRED's calendar states none.
    [MaxLength(64)]
    public string ReferencePeriod { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
