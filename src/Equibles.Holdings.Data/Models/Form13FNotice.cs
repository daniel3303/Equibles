using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A Form 13F notice (13F-NT): a manager stating that other managers report its holdings for the
/// quarter. It is identity evidence only; a notice filer is not a 13F filer and never becomes an
/// <see cref="InstitutionalHolder"/> through it.
/// </summary>
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(FilerCik), nameof(ReportDate))]
public class Form13FNotice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>The notice filer's CIK, without leading zeros.</summary>
    [Required]
    [MaxLength(16)]
    public string FilerCik { get; set; }

    [MaxLength(512)]
    public string FilerName { get; set; }

    /// <summary>The period of report as filed.</summary>
    public DateOnly ReportDate { get; set; }

    public DateOnly FilingDate { get; set; }
    public bool IsAmendment { get; set; }

    /// <summary>The filer's own Form 13F file number ("028-…") from the cover page.</summary>
    [MaxLength(32)]
    public string Form13FFileNumber { get; set; }

    [MaxLength(32)]
    public string CrdNumber { get; set; }

    [MaxLength(32)]
    public string SecFileNumber { get; set; }

    /// <summary>The 13F data set the notice was read from.</summary>
    [Required]
    [MaxLength(128)]
    public string DataSetFileName { get; set; }

    /// <summary>The managers the notice says report for the filer, in filed order.</summary>
    public virtual List<Form13FNoticeReporter> Reporters { get; set; } = [];
}
