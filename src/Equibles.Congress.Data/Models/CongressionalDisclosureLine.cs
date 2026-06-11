using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// One asset or liability row of an annual disclosure, kept at the disclosed
/// grain: a free-text description and the checked value range. Ranges are the
/// form's own brackets ($1,000,001–$5,000,000); no point estimate is derived
/// at this level.
/// </summary>
[Index(nameof(CongressionalAnnualDisclosureId))]
public class CongressionalDisclosureLine
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CongressionalAnnualDisclosureId { get; set; }
    public virtual CongressionalAnnualDisclosure CongressionalAnnualDisclosure { get; set; }

    public CongressionalDisclosureLineKind Kind { get; set; }

    [Required]
    [MaxLength(512)]
    public string Description { get; set; }

    /// <summary>Lower bound of the disclosed range, in dollars.</summary>
    public long RangeMinimum { get; set; }

    /// <summary>Upper bound of the disclosed range, in dollars.</summary>
    public long RangeMaximum { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
