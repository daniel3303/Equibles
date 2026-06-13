using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.FdaCatalysts.Data.Models;

/// <summary>
/// A scheduled FDA regulatory catalyst for a public company. Today this holds FDA
/// advisory-committee (AdComm) meetings, with the type discriminator left open for
/// PDUFA decisions and complete-response follow-ups once an authoritative source for
/// those exists. AdComm meetings are sourced from the Federal Register, which publishes
/// FDA advisory-committee meeting notices as structured documents.
/// </summary>
[Index(nameof(SourceReference), IsUnique = true)]
[Index(nameof(CatalystType), nameof(MeetingDate))]
[Index(nameof(MeetingDate))]
[Index(nameof(CommonStockId))]
public class FdaCatalyst
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public FdaCatalystType CatalystType { get; set; }

    public DateOnly MeetingDate { get; set; }

    [Required]
    public string Committee { get; set; }

    [Required]
    public string Title { get; set; }

    public string Summary { get; set; }

    // The Federal Register document number — globally unique per notice, so it is the
    // natural key that keeps re-imports of the same meeting idempotent.
    [Required]
    [MaxLength(64)]
    public string SourceReference { get; set; }

    public string SourceUrl { get; set; }

    public DateOnly? PublicationDate { get; set; }

    // Resolved to a CommonStock only when the notice's sponsor maps authoritatively
    // (CIK / ticker), and left null otherwise — many sponsors are private, pre-IPO, or
    // foreign and fall outside the tracked equity universe, so a soft reference avoids
    // dropping catalysts we cannot tie to a ticker.
    public Guid? CommonStockId { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
