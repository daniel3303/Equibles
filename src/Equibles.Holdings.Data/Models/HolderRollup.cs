using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A filer's flattened parent chain: its direct 13F reporting parent and the ultimate parent it
/// groups under, chosen by precedence (GLEIF ultimate parent, then the GLEIF direct-parent chain,
/// then the 13F reporting chain, then itself). One row per 13F filer CIK and per CIK any
/// <see cref="HolderLinkEvidence"/> names; a CIK without a row rolls up to itself.
/// </summary>
/// <remarks>
/// Two parents at the deciding tier set <see cref="HasConflictingEvidence"/> and leave the filer on
/// itself, as does a cycle; a chain stops after four parents.
/// </remarks>
[Index(nameof(UltimateCik))]
[Index(nameof(ReportingParentCik))]
public class HolderRollup
{
    [Key]
    [MaxLength(16)]
    public string Cik { get; set; }

    /// <summary>The single 13F filer that reports this one's holdings, whichever tier decides the ultimate parent.</summary>
    [MaxLength(16)]
    public string ReportingParentCik { get; set; }

    [Required]
    [MaxLength(16)]
    public string UltimateCik { get; set; }

    /// <summary>The 13F filer stored under <see cref="UltimateCik"/>, when there is exactly one.</summary>
    public Guid? UltimateHolderId { get; set; }

    public HolderRollupBasis Basis { get; set; }

    public bool HasConflictingEvidence { get; set; }

    public int DerivationVersion { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
