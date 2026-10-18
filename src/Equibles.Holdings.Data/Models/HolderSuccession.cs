using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A filer whose 13F reports continue under another CIK: the predecessor files no 13F after
/// <see cref="PredecessorLastReportDate"/>, the successor's first 13F is the next quarter, and a
/// filing ties the two by CIK, 13F file number, CRD or SEC file number, never by name.
/// </summary>
[Index(nameof(PredecessorHolderId), nameof(SuccessorHolderId), IsUnique = true)]
[Index(nameof(SuccessorHolderId))]
public class HolderSuccession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PredecessorHolderId { get; set; }
    public virtual InstitutionalHolder PredecessorHolder { get; set; }

    public Guid SuccessorHolderId { get; set; }
    public virtual InstitutionalHolder SuccessorHolder { get; set; }

    public DateOnly PredecessorLastReportDate { get; set; }
    public DateOnly SuccessorFirstReportDate { get; set; }

    public HolderSuccessionBasis Basis { get; set; }

    /// <summary>A filing that states the tie: the notice or 13F report the strongest basis was read from.</summary>
    [MaxLength(32)]
    public string EvidenceAccession { get; set; }

    /// <summary>
    /// The tracked 13F value of this pair's whole group, the pairs joined by a shared filer in
    /// the same successor quarter: every predecessor's last quarter and every successor's first.
    /// </summary>
    public long PredecessorGroupValue { get; set; }

    /// <summary>The successors' side of <see cref="PredecessorGroupValue"/>.</summary>
    public long SuccessorGroupValue { get; set; }

    public int DerivationVersion { get; set; }
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
