using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.EquityMarkets.Data.Models;

public enum FirdsFileKind
{
    Full = 0,
    Delta = 1,
}

// Completion marker per published file, so a restart never re-reads a file and the newest full set is known.
[Index(nameof(Authority), nameof(FileName), IsUnique = true)]
[Index(nameof(Authority), nameof(Kind), nameof(PublishedOn))]
public class FirdsImportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(4)]
    public string Authority { get; set; }

    public FirdsFileKind Kind { get; set; }

    [Required, MaxLength(64)]
    public string FileName { get; set; }

    public DateOnly PublishedOn { get; set; }

    [MaxLength(64)]
    public string Checksum { get; set; }

    public DateTime ImportedAt { get; set; }
    public int RowsRead { get; set; }
    public int RowsStored { get; set; }
}
