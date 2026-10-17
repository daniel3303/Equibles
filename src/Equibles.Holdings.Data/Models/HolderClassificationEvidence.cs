using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One regulatory fact about a holder, stored as filed (a Form ADV registration, a SIC code, a
/// 13D/13G type code) and tied to it by a filed identifier. The type has no name field, so a
/// classification can never be read from a holder's name.
/// </summary>
[Index(nameof(InstitutionalHolderId))]
public class HolderClassificationEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }

    public HolderEvidenceSource Source { get; set; }
    public HolderEvidenceMatch MatchedOn { get; set; }

    /// <summary>The filed field, e.g. <c>Registration</c>, <c>Sic</c> or <c>TypeOfReportingPerson</c>.</summary>
    [Required]
    [MaxLength(64)]
    public string Field { get; set; }

    [MaxLength(256)]
    public string Value { get; set; }

    /// <summary>The record's own key: a CRD, a CIK or an accession number.</summary>
    [Required]
    [MaxLength(32)]
    public string SourceKey { get; set; }

    /// <summary>The date the fact was true as of: a roster or report date, or a filing date.</summary>
    public DateOnly AsOf { get; set; }
}
