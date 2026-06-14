using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.FdaCatalysts.Data.Models;

/// <summary>
/// A scheduled FDA regulatory catalyst for a public company. Today this holds FDA
/// advisory-committee (AdComm) meetings, with the type discriminator left open for
/// PDUFA decisions and complete-response follow-ups once an authoritative source for
/// those exists. AdComm meetings are sourced from the FDA.gov advisory-committee
/// calendar, which publishes the meeting schedule as a structured table with explicit
/// Start Date / End Date / Meeting / Center columns and a stable per-meeting page slug.
/// </summary>
[Index(nameof(SourceReference), IsUnique = true)]
[Index(nameof(CatalystType), nameof(MeetingDate))]
[Index(nameof(MeetingDate))]
[Index(nameof(CommonStockId))]
public class FdaCatalyst
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public FdaCatalystType CatalystType { get; set; }

    // The Start Date column of the FDA calendar (the first day of the meeting).
    public DateOnly MeetingDate { get; set; }

    // The End Date column; null when the calendar lists no distinct end (single-day
    // meetings or rows where the end is not given).
    public DateOnly? EndDate { get; set; }

    // The FDA calendar's organizational column (e.g. "Center for Drug Evaluation and
    // Research"). The standalone committee name is not its own column — it appears only
    // inside the Meeting title prose — so Center is the authoritative org field and the
    // committee is carried in Title.
    [Required]
    public string Center { get; set; }

    [Required]
    public string Title { get; set; }

    public string Summary { get; set; }

    // The FDA calendar's per-meeting page slug (e.g.
    // "july-23-24-2026-meeting-pharmacy-compounding-advisory-committee-07232026") —
    // globally unique per meeting, so it is the natural key that keeps re-imports of the
    // same meeting idempotent.
    [Required]
    [MaxLength(128)]
    public string SourceReference { get; set; }

    public string SourceUrl { get; set; }

    // Resolved to a CommonStock only when the meeting names a sponsor that maps
    // authoritatively (CIK / ticker), and left null otherwise — many meetings concern
    // private, pre-IPO, or foreign sponsors that fall outside the tracked equity
    // universe, so a soft reference avoids dropping catalysts we cannot tie to a ticker.
    public Guid? CommonStockId { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
