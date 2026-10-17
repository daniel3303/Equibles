using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// An investment adviser or sub-adviser of a <see cref="NCenFundReport"/>, with the CRD number,
/// SEC file number and LEI the fund reports for it.
/// </summary>
[Index(nameof(FundReportId))]
[Index(nameof(Crd))]
public class NCenFundAdviser
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FundReportId { get; set; }
    public virtual NCenFundReport FundReport { get; set; }

    public NCenAdviserRole Role { get; set; }

    [MaxLength(512)]
    public string Name { get; set; }

    /// <summary>The adviser's SEC file number as filed, e.g. "801-11953".</summary>
    [MaxLength(32)]
    public string FileNumber { get; set; }

    /// <summary>The adviser's CRD number, in the same form as <see cref="FormAdvAdviser.Crd"/>.</summary>
    public int? Crd { get; set; }

    [MaxLength(20)]
    public string Lei { get; set; }

    [MaxLength(16)]
    public string State { get; set; }

    [MaxLength(16)]
    public string Country { get; set; }

    /// <summary>For a sub-adviser: whether it is an affiliated person of the fund's adviser.</summary>
    public bool? IsAffiliated { get; set; }

    /// <summary>Whether the adviser was hired during the reporting period.</summary>
    public bool? IsHiredDuringPeriod { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? TerminationDate { get; set; }
}
