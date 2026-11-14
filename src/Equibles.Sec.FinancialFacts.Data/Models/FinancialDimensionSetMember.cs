using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// One (axis, member) pair of a <see cref="FinancialDimensionSet"/>, as full XBRL QNames
/// (<c>srt:ProductOrServiceAxis</c> → <c>aapl:IPhoneMember</c>).
/// </summary>
[PrimaryKey(nameof(DimensionSetKey), nameof(Axis), nameof(Member))]
[Index(nameof(Axis), nameof(Member))]
public class FinancialDimensionSetMember
{
    [MaxLength(64)]
    public string DimensionSetKey { get; set; }
    public virtual FinancialDimensionSet DimensionSet { get; set; }

    [Required]
    [MaxLength(256)]
    public string Axis { get; set; }

    [Required]
    [MaxLength(256)]
    public string Member { get; set; }
}
