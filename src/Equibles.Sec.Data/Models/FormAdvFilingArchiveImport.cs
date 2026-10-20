using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One imported part of SEC's Form ADV filing archive (registered or exempt reporting advisers,
/// filings through <see cref="ArchiveEndDate"/>). A part is read again only when the importer's
/// version moves past <see cref="ParserVersion"/>.
/// </summary>
[Index(nameof(Registration), nameof(ArchiveEndDate), IsUnique = true)]
public class FormAdvFilingArchiveImport
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public FormAdvRegistration Registration { get; set; }

    public DateOnly ArchiveEndDate { get; set; }

    public int ParserVersion { get; set; }

    /// <summary>Filings kept after collapsing repeats.</summary>
    public int FilingCount { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
