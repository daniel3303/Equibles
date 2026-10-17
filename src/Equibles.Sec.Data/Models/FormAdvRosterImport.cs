using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One imported Form ADV roster snapshot. A snapshot is skipped only when it was imported by
/// the current parser version, so a parser change re-reads the newest roster once.
/// </summary>
[Index(nameof(Registration), nameof(ReportDate), IsUnique = true)]
public class FormAdvRosterImport
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public FormAdvRegistration Registration { get; set; }

    public DateOnly ReportDate { get; set; }

    public int ParserVersion { get; set; }

    public int AdviserCount { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
