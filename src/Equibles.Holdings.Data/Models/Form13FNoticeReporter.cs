using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One manager a 13F notice names as reporting the filer's holdings, with the identifiers filed
/// beside its name. Only the identifiers are joinable; a row with a name alone is display only.
/// </summary>
[Index(nameof(Form13FNoticeId), nameof(Ordinal), IsUnique = true)]
[Index(nameof(Cik))]
[Index(nameof(Form13FFileNumber))]
public class Form13FNoticeReporter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid Form13FNoticeId { get; set; }
    public virtual Form13FNotice Notice { get; set; }

    /// <summary>The manager's 1-based position on the notice's cover page.</summary>
    public int Ordinal { get; set; }

    [MaxLength(256)]
    public string Name { get; set; }

    /// <summary>Central index key without leading zeros, or null when none was filed.</summary>
    [MaxLength(16)]
    public string Cik { get; set; }

    [MaxLength(32)]
    public string Form13FFileNumber { get; set; }

    [MaxLength(32)]
    public string CrdNumber { get; set; }

    [MaxLength(32)]
    public string SecFileNumber { get; set; }
}
