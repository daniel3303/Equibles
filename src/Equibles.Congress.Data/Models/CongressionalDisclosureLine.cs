using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// One asset or liability row of an annual disclosure, kept at the disclosed
/// grain: a free-text description and the checked value range. Ranges are the
/// form's own brackets ($1,000,001–$5,000,000); no point estimate is derived
/// at this level.
/// </summary>
[Index(nameof(CongressionalAnnualDisclosureId))]
public class CongressionalDisclosureLine
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CongressionalAnnualDisclosureId { get; set; }
    public virtual CongressionalAnnualDisclosure CongressionalAnnualDisclosure { get; set; }

    public CongressionalDisclosureLineKind Kind { get; set; }

    [Required]
    [MaxLength(512)]
    public string Description { get; set; }

    /// <summary>Lower bound of the disclosed range, in dollars.</summary>
    public long RangeMinimum { get; set; }

    /// <summary>Upper bound of the disclosed range, in dollars.</summary>
    public long RangeMaximum { get; set; }

    /// <summary>
    /// The asset class the filer declared, verbatim in the filing's own
    /// vocabulary: a House bracketed code with its brackets removed ("ST",
    /// "RP", "BA") or the Senate's spelled-out label ("Bank Deposit", "Corporate
    /// Securities Non-Public Stock"). The two chambers publish different
    /// vocabularies and neither form carries a legend, so no cross-chamber
    /// mapping is invented here. Null on liability rows and on filings parsed
    /// before this was captured.
    /// </summary>
    [MaxLength(128)]
    public string AssetType { get; set; }

    /// <summary>
    /// The income categories the asset produced, verbatim ("Dividends",
    /// "Rent", "Dividends, Capital Gains"). Null when the filer disclosed none.
    /// </summary>
    [MaxLength(256)]
    public string IncomeType { get; set; }

    /// <summary>
    /// Lower bound of the income the asset produced over the year, in dollars.
    /// Null when the filer disclosed no income bracket — never zero, which is
    /// itself a disclosed value.
    /// </summary>
    public long? IncomeMinimum { get; set; }

    /// <summary>Upper bound of the disclosed income range, in dollars.</summary>
    public long? IncomeMaximum { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
