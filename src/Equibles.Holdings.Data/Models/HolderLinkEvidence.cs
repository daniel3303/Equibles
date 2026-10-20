using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One filed statement that ties a child filer to a parent by identifier (CIK, then 13F file
/// number, then CRD for SEC filings; LEI for GLEIF), never by name. The 13F kinds come from the
/// recent closed quarters' other-manager lists and notices; the GLEIF kinds come from an optional
/// <c>IHolderLinkEvidenceSource</c>.
/// </summary>
/// <remarks>
/// CIKs are canonical (no leading zeros) and holder ids are plain columns: the table is replaced
/// wholesale on every refresh, and a child that never filed a 13F report itself (a notice filer)
/// has a CIK but no holder. A pair the succession rule ties is never stored, since a CIK change is
/// not a parent.
/// </remarks>
[Index(nameof(ChildCik), nameof(Kind), nameof(ReportDate))]
[Index(nameof(ParentCik), nameof(ReportDate))]
public class HolderLinkEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(16)]
    public string ChildCik { get; set; }

    public Guid? ChildHolderId { get; set; }

    /// <summary>Null only for a GLEIF parent whose LEI maps to no CIK.</summary>
    [MaxLength(16)]
    public string ParentCik { get; set; }

    public Guid? ParentHolderId { get; set; }

    [MaxLength(20)]
    public string ChildLei { get; set; }

    [MaxLength(20)]
    public string ParentLei { get; set; }

    public HolderLinkKind Kind { get; set; }

    /// <summary>The accession of the 13F filing or notice, or the child LEI for a GLEIF record.</summary>
    [Required]
    [MaxLength(32)]
    public string SourceKey { get; set; }

    /// <summary>The 13F quarter the statement covers; null for GLEIF records, which are current.</summary>
    public DateOnly? ReportDate { get; set; }

    /// <summary>When the statement was filed or fetched.</summary>
    public DateOnly AsOf { get; set; }
}
