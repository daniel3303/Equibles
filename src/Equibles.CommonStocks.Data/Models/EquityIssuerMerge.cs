using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// Audit of a duplicate issuer folded into the issuer that represents the same company; the retired id never returns.
[Index(nameof(SurvivingIssuerId))]
[Index(nameof(CustomerReferencesRepointedAt))]
public class EquityIssuerMerge
{
    [Key]
    public Guid RetiredIssuerId { get; set; }

    public Guid SurvivingIssuerId { get; set; }
    public virtual EquityIssuer SurvivingIssuer { get; set; }

    [Required, MaxLength(64)]
    public string EvidenceKind { get; set; }

    [Required]
    public List<Guid> EvidenceSourceRecordIds { get; set; } = [];

    [Required, Column(TypeName = "jsonb")]
    public string RetiredIssuerJson { get; set; }

    [Required, Column(TypeName = "jsonb")]
    public string ReportJson { get; set; }

    [Required, MaxLength(256)]
    public string MergedBy { get; set; }

    public DateTime MergedAt { get; set; }

    // Set once every reference outside the financial database follows the survivor.
    public DateTime? CustomerReferencesRepointedAt { get; set; }
}
