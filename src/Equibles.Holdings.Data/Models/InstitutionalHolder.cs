using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Index(nameof(Cik), IsUnique = true)]
[Index(nameof(Name))]
[Index(nameof(Classification))]
public class InstitutionalHolder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(16)]
    public string Cik { get; set; }

    [MaxLength(512)]
    public string Name { get; set; }

    [MaxLength(128)]
    public string City { get; set; }

    [MaxLength(64)]
    public string StateOrCountry { get; set; }

    [MaxLength(32)]
    public string Form13FFileNumber { get; set; }

    [MaxLength(32)]
    public string CrdNumber { get; set; }

    /// <summary>The filer's own SEC file number (801- for a registered adviser) from its cover page.</summary>
    [MaxLength(32)]
    public string SecFileNumber { get; set; }

    /// <summary>
    /// Accession of the 13F cover page the identity numbers were last read from; null until a
    /// cover has been applied since the column existed.
    /// </summary>
    [MaxLength(32)]
    public string IdentityAccession { get; set; }

    public FundClassification Classification { get; set; }

    public bool ConfidentialTreatmentRequested { get; set; }

    public virtual List<InstitutionalHolding> Holdings { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
