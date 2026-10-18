using System.ComponentModel.DataAnnotations;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// A member of Congress as published by the public congress-legislators roster, keyed by the
/// official Biographical Directory id. It carries no link to a filing member; a
/// <see cref="CongressMember"/> refers to it only through <see cref="CongressMember.BioguideId"/>.
/// </summary>
public class CongressLegislator
{
    [Key]
    [MaxLength(7)]
    public string BioguideId { get; set; }

    [Required]
    [MaxLength(128)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(128)]
    public string LastName { get; set; }

    [MaxLength(16)]
    public string Suffix { get; set; }

    [MaxLength(256)]
    public string OfficialFullName { get; set; }

    public virtual List<CongressMemberTerm> Terms { get; set; } = [];

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
