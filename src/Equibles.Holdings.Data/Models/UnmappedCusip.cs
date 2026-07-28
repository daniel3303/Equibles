using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A security a filer reported that matches no stock we track, recorded once per quarter it
/// appears in so the gap is visible and rankable instead of silent.
/// </summary>
/// <remarks>
/// <para>
/// The import can only attach a position to a stock it can identify by CUSIP, so a row whose CUSIP
/// resolves to nothing is left out. Most of those are genuinely out of scope — bonds, warrants,
/// funds we do not cover. Some are not: a corporate action can retire a CUSIP and issue a new one
/// for the same company, and until the old identifier is recorded as an alias every position filed
/// under it disappears. Scion's Hudson Pacific position vanished from its Q2 2024 filing exactly
/// that way, after the issuer's reverse split changed its CUSIP.
/// </para>
/// <para>
/// Before this, the only trace was a per-import count of skipped rows, which cannot distinguish
/// the bonds we never wanted from the equity we lost. Keyed per quarter rather than accumulated,
/// so re-importing a data set restates a row instead of inflating it, and the dollars carried tell
/// an operator which identifiers are worth chasing first.
/// </para>
/// </remarks>
[Index(nameof(Cusip), nameof(ReportDate), IsUnique = true)]
[Index(nameof(FiledValue))]
public class UnmappedCusip
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(9)]
    public string Cusip { get; set; }

    public DateOnly ReportDate { get; set; }

    /// <summary>
    /// The issuer name as filed. Null where the source carries no name column (the realtime
    /// archive), so it is a hint for a human rather than anything to match on.
    /// </summary>
    [MaxLength(256)]
    public string IssuerName { get; set; }

    /// <summary>Positions filed under this CUSIP for the quarter.</summary>
    public int Positions { get; set; }

    /// <summary>
    /// Total value filed under this CUSIP for the quarter, in dollars. The materiality ranking:
    /// the filed figure is the only value available, since nothing was derived for these rows.
    /// </summary>
    public long FiledValue { get; set; }

    /// <summary>When this row was last written. The import replaces a quarter's rows wholesale,
    /// so it doubles as "still unmapped as of".</summary>
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
