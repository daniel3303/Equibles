using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One manager a 13F filing names besides the filer itself, with the identifiers the SEC filed
/// alongside the name. The filing-level half of the manager split: a position's per-manager legs
/// live on <see cref="HoldingManagerEntry"/> and carry only a sequence number, and this table is
/// what turns that number into an institution.
/// </summary>
/// <remarks>
/// <para>
/// A large holding company does not file one 13F per subsidiary. It files a single combination
/// report covering all of them and attributes each position to a numbered entry in its
/// other-manager table — so Goldman Sachs Group's filing carries eight managers, and every
/// position it reports points at one of them. Without the identifiers, that structure is invisible:
/// a holder row is one opaque total, and the subsidiary that actually holds the shares cannot be
/// linked to its own filings.
/// </para>
/// <para>
/// The names alone are not enough to recover it. Matching institutions on name is guesswork that
/// silently merges unrelated firms, so only the filed identifiers are stored and only they are
/// joined on: CIK first, then the Form 13F file number, then the CRD. The SEC lets all three be
/// blank, which is why a row can carry a name and nothing else — such a row is displayable but not
/// linkable, and that is the intended outcome rather than a reason to fall back to name matching.
/// </para>
/// <para>
/// Rows are replaced per accession on every import, so a re-import restates a filing's list rather
/// than duplicating it. They are never swept for accessions outside the import: a "new holdings"
/// amendment leaves a holder's quarter spanning several accessions, and the positions that kept the
/// original's accession still need its manager list to resolve.
/// </para>
/// </remarks>
[Index(nameof(AccessionNumber), nameof(Direction), nameof(SequenceNumber), IsUnique = true)]
[Index(nameof(Cik))]
public class FilingOtherManager
{
    // Client-generated, never database-generated: the flush upserts these rows, and the upsert
    // must be able to send the id it generated instead of expecting a database default.
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The filing that named this manager.</summary>
    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>Which page of the filing named it, and so which way the relationship points.</summary>
    public OtherManagerDirection Direction { get; set; }

    /// <summary>
    /// For <see cref="OtherManagerDirection.IncludedInReport"/>, the filing's own sequence number —
    /// the value a position's <see cref="HoldingManagerEntry.ManagerNumber"/> refers to. The cover
    /// page's list is sequence-less, so those rows carry a 1-based ordinal in filed order instead,
    /// which keeps the column non-null and the list stably ordered without inventing a filed value.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// Central index key, leading zeros trimmed to match how filer CIKs are stored elsewhere.
    /// Null when the filing did not carry one.
    /// </summary>
    [MaxLength(16)]
    public string Cik { get; set; }

    /// <summary>Form 13F file number ("028-…"). The fallback identity when no CIK was filed.</summary>
    [MaxLength(32)]
    public string Form13FFileNumber { get; set; }

    /// <summary>CRD number, where the manager is a registered adviser.</summary>
    [MaxLength(32)]
    public string CrdNumber { get; set; }

    /// <summary>SEC file number ("801-…"). Absent from the realtime filing XML, so null on rows
    /// written before the quarterly data set restates them.</summary>
    [MaxLength(32)]
    public string SecFileNumber { get; set; }

    /// <summary>The manager's name as filed. Display only — never matched on.</summary>
    [MaxLength(256)]
    public string Name { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
