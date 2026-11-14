using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// One member's annual financial disclosure (Form A / eFD annual report) for one
/// calendar year, ingested from electronically-filed reports only — scanned
/// paper filings are never parsed, so a missing (member, year) row means "no
/// electronic filing", not zero net worth. Disclosed values are ranges; the
/// rollup is a band: <see cref="NetWorthMinimum"/> sums asset minimums minus
/// liability maximums, <see cref="NetWorthMaximum"/> asset maximums minus
/// liability minimums. A bracket without a ceiling ("Over $50,000,000") leaves
/// that side of the band unbounded, which the two open-ended flags record.
/// Amendments replace the year's report in place — the latest filed report is
/// the one represented here.
/// </summary>
[Index(nameof(CongressMemberId), nameof(Year), IsUnique = true)]
[Index(nameof(Year))]
public class CongressionalAnnualDisclosure
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CongressMemberId { get; set; }
    public virtual CongressMember CongressMember { get; set; }

    /// <summary>The calendar year the report covers (not the filing year).</summary>
    public int Year { get; set; }

    public DateOnly FiledDate { get; set; }

    /// <summary>
    /// The source system's identifier for the filed report (House Clerk DocID /
    /// Senate eFD report id) — the provenance pointer for the stored lines.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ReportId { get; set; }

    /// <summary>Lower bound of the net-worth band, in dollars.</summary>
    public long NetWorthMinimum { get; set; }

    /// <summary>Upper bound of the net-worth band, in dollars.</summary>
    public long NetWorthMaximum { get; set; }

    /// <summary>
    /// A liability is disclosed with no ceiling, so net worth may be below
    /// <see cref="NetWorthMinimum"/>, which counts that liability at its floor.
    /// </summary>
    public bool NetWorthMinimumOpenEnded { get; set; }

    /// <summary>
    /// An asset is disclosed with no ceiling, so net worth has no upper bound:
    /// <see cref="NetWorthMaximum"/> counts that asset at its floor.
    /// </summary>
    public bool NetWorthMaximumOpenEnded { get; set; }

    public virtual List<CongressionalDisclosureLine> Lines { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
