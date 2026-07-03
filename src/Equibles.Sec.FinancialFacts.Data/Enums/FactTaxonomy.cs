using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Enums;

/// <summary>
/// The XBRL taxonomy a financial concept belongs to. The first five map to the
/// top-level keys under <c>facts</c> in SEC's Company Facts API
/// (<c>us-gaap</c>, <c>dei</c>, <c>ifrs-full</c>, <c>srt</c>, <c>invest</c>).
/// <see cref="Custom"/> holds filer-extension concepts — the company's own
/// namespace tags (KPIs like subscriber counts, ARR, deliveries) that only the
/// raw-XBRL extraction path can supply; the Company Facts API never carries
/// them. Facts in reference taxonomies outside this set (country, currency,
/// exch, …) are skipped on import.
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

    /// <summary>
    /// A filer-extension concept from the company's own taxonomy. The
    /// <c>FinancialConcept.Tag</c> keeps the QName shape
    /// (<c>adbe:AnnualizedRecurringRevenue</c>) so concepts from different
    /// companies never collide on the (Taxonomy, Tag) unique index.
    /// </summary>
    [Display(Name = "Company-specific")]
    Custom,
}
