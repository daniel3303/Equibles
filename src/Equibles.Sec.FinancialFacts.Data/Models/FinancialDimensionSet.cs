using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// One distinct combination of explicit XBRL dimensions, stored once and shared by every
/// <see cref="FinancialFact"/> whose <see cref="FinancialFact.DimensionsKey"/> equals
/// <see cref="Key"/>. The key is a hash of the members, so a stored set never changes.
/// </summary>
public class FinancialDimensionSet
{
    [Key]
    [MaxLength(64)]
    public string Key { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    public virtual List<FinancialDimensionSetMember> Members { get; set; } = [];
}
