using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// One explicit XBRL dimension on a <see cref="FinancialFact"/> — the
/// (axis, member) pair that disambiguates a segment / product / geography cut
/// from the consolidated total (e.g. <c>srt:ProductOrServiceAxis</c> →
/// <c>aapl:IPhoneMember</c>). Facts with no rows here are the consolidated
/// (no-dimension) default; the SEC Company Facts API only returns facts in
/// that default context, so dimensional facts arrive exclusively from the
/// iXBRL / standalone-XBRL extractor (see #877). Axis and member are stored
/// as full XBRL QNames (<c>prefix:localName</c>).
/// </summary>
[Index(nameof(FinancialFactId), nameof(Axis), nameof(Member), IsUnique = true)]
[Index(nameof(Axis), nameof(Member))]
public class FinancialFactDimension
{
    // Client-generated Guid key — matches the rest of the financial-facts
    // schema so FlexLabs UpsertRange includes the column on INSERT.
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FinancialFactId { get; set; }
    public virtual FinancialFact FinancialFact { get; set; }

    /// <summary>XBRL axis QName, e.g. <c>srt:ProductOrServiceAxis</c>.</summary>
    [Required]
    [MaxLength(256)]
    public string Axis { get; set; }

    /// <summary>XBRL member QName, e.g. <c>aapl:IPhoneMember</c>.</summary>
    [Required]
    [MaxLength(256)]
    public string Member { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
