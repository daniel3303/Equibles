using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The cover-page facts of one Schedule 13D or 13G accession and every reporting person it names.
/// Holdings rows keep only the lead person's position; this keeps who reported and as what.
/// </summary>
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(FilerCik))]
[Index(nameof(IssuerCik))]
public class BeneficialOwnershipFiling
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public FilingType FilingType { get; set; }
    public bool IsAmendment { get; set; }
    public DateOnly FilingDate { get; set; }

    /// <summary>The cover-page event date, or null when it is unreadable.</summary>
    public DateOnly? DateOfEvent { get; set; }

    /// <summary>The submitting filer's CIK, without leading zeros.</summary>
    [MaxLength(16)]
    public string FilerCik { get; set; }

    [MaxLength(16)]
    public string IssuerCik { get; set; }

    [MaxLength(16)]
    public string IssuerCusip { get; set; }

    /// <summary>The Rule 13d-1 paragraphs a 13G is filed under; none on a 13D.</summary>
    public Schedule13GRules Rules { get; set; }

    /// <summary>A 13G's Item 3 categories of the persons filing under Rule 13d-1(b).</summary>
    public ReportingPersonTypes FilingPersonTypes { get; set; }

    /// <summary>Item 3 codes SEC's list does not define, as filed.</summary>
    [MaxLength(64)]
    public string UnrecognizedFilingPersonTypeCodes { get; set; }

    /// <summary>
    /// True when EDGAR no longer serves the document or it cannot be parsed; such a row has no
    /// persons and only stops the detail sync from fetching it again.
    /// </summary>
    public bool IsUnreadable { get; set; }

    /// <summary>
    /// The text reader version that read a pre-XML filing (form SC 13D or SC 13G); null for a
    /// filing read from structured XML.
    /// </summary>
    public int? ParserVersion { get; set; }

    /// <summary>
    /// Why the text reader refused the filing, which is then unreadable and parked under
    /// <see cref="ParserVersion"/>: a later reader version reads it again.
    /// </summary>
    [MaxLength(128)]
    public string RefusalReason { get; set; }

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;

    public virtual List<BeneficialOwnershipReportingPerson> ReportingPersons { get; set; } = [];
}
