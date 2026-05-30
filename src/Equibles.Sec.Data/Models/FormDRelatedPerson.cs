using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A person named in the related-persons section of a <see cref="FormDFiling"/> — an executive
/// officer, director or promoter of the issuer. Form D carries the name and relationship(s) but
/// no CIK, so the name is stored as free text rather than resolved to another entity.
/// </summary>
[Index(nameof(FormDFilingId))]
public class FormDRelatedPerson
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FormDFilingId { get; set; }
    public virtual FormDFiling FormDFiling { get; set; }

    /// <summary>The person's full name, assembled from the filing's first/middle/last name parts.</summary>
    [MaxLength(512)]
    public string Name { get; set; }

    /// <summary>
    /// The person's relationship(s) to the issuer (e.g. "Executive Officer", "Director",
    /// "Promoter"). A person can hold several — joined with ", ".
    /// </summary>
    [MaxLength(256)]
    public string Relationships { get; set; }
}
