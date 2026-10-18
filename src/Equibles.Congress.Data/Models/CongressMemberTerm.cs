using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// One continuous stretch a legislator served in one chamber under one party, covering the
/// half-open range [<see cref="StartDate"/>, <see cref="EndDate"/>): the end date belongs to the
/// successor, so a seat changing hands on January 3 is held by the incoming member that day.
/// A term with a mid-term party switch is stored as one row per party, and a legislator's rows
/// never overlap.
/// </summary>
[Index(nameof(BioguideId), nameof(Chamber), nameof(StartDate), IsUnique = true)]
[Index(nameof(Chamber), nameof(State), nameof(District), nameof(StartDate))]
public class CongressMemberTerm
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(7)]
    public string BioguideId { get; set; }

    [ForeignKey(nameof(BioguideId))]
    public virtual CongressLegislator Legislator { get; set; }

    public CongressPosition Chamber { get; set; }

    /// <summary>The two-letter postal code of the state or territory.</summary>
    [Required]
    [Column(TypeName = "character(2)")]
    public string State { get; set; }

    /// <summary>
    /// The House district, 0 for an at-large seat or a non-voting delegate (DC, PR, GU, VI, AS,
    /// MP); null for a senator.
    /// </summary>
    public short? District { get; set; }

    /// <summary>The party exactly as the roster publishes it ("Democrat", "Independent").</summary>
    [Required]
    [MaxLength(32)]
    public string Party { get; set; }

    /// <summary>The party an independent caucuses with, when the roster states one.</summary>
    [MaxLength(32)]
    public string Caucus { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>The first day the legislator no longer holds this term (exclusive).</summary>
    public DateOnly EndDate { get; set; }
}
