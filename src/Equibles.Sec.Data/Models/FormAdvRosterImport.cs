using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One Form ADV roster snapshot. <see cref="ParserVersion"/> records that the snapshot updated the
/// current adviser view; a snapshot is skipped only when the current parser version imported it,
/// so a parser change re-reads the newest roster once. <see cref="VersionedAt"/> records that the
/// snapshot was folded into the adviser version history, which takes each roster kind strictly
/// oldest first. A history-only snapshot keeps <see cref="ParserVersion"/> 0.
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

    /// <summary>When the snapshot was folded into the version history; null until then.</summary>
    public DateTime? VersionedAt { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
