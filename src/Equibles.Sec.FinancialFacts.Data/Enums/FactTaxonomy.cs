using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Enums;

/// <summary>
/// The XBRL taxonomy a financial concept belongs to. Maps to the top-level
/// keys under <c>facts</c> in SEC's Company Facts API
/// (<c>us-gaap</c>, <c>dei</c>, <c>ifrs-full</c>, <c>srt</c>, <c>invest</c>).
/// Facts in taxonomies outside this set are skipped on import.
/// </summary>
public enum FactTaxonomy
{
    [Display(Name = "US-GAAP")]
    UsGaap,

    [Display(Name = "DEI")]
    Dei,

    [Display(Name = "IFRS-Full")]
    IfrsFull,

    [Display(Name = "SRT")]
    Srt,

    [Display(Name = "INVEST")]
    Invest,
}
