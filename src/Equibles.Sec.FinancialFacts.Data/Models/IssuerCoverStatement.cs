using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// What one periodic report's cover page states about its registrant: the SEC file number it
/// reports under (<c>dei:EntityFileNumber</c>) and whether it checked the shell-company box
/// (<c>dei:EntityShellCompany</c>), from the filing's raw XBRL envelope. One row per filing, so
/// every statement keeps its own filing date and accession and a reader can ask what the newest
/// cover said as of any date; a later report never overwrites an earlier one.
/// </summary>
[Index(nameof(EquityIssuerId), nameof(FiledDate))]
[Index(nameof(DocumentId), IsUnique = true)]
public class IssuerCoverStatement
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }

    /// <summary>The filing the statement was read from; the row is derived from it.</summary>
    public Guid DocumentId { get; set; }
    public virtual Document Document { get; set; }

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>The filing's form, e.g. 10-K or 10-Q.</summary>
    [Required]
    public DocumentType Form { get; set; }

    public DateOnly FiledDate { get; set; }

    /// <summary>The <c>dei:EntityFileNumber</c> as filed; null when the cover states none or two.</summary>
    [MaxLength(32)]
    public string FileNumber { get; set; }

    /// <summary>The decoded <c>dei:EntityShellCompany</c>; null when it is not stated unambiguously.</summary>
    public bool? IsShellCompany { get; set; }
}
