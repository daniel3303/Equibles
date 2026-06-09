using System.ComponentModel.DataAnnotations;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// The platform or CMS a company's investor-relations website runs on, detected
/// from the IR page HTML. Determines which scraper implementation handles each
/// company's IR content.
/// </summary>
public enum IrPlatformType
{
    /// <summary>Not yet classified, or the company has no discovered IR page.</summary>
    [Display(Name = "Unknown")]
    Unknown = 0,

    /// <summary>Q4 Inc investor-relations platform (assets served from q4cdn.com).</summary>
    [Display(Name = "Q4 Inc")]
    Q4Inc = 1,

    /// <summary>Nasdaq IR Insight investor-relations platform.</summary>
    [Display(Name = "Nasdaq IR Insight")]
    NasdaqIrInsight = 2,

    /// <summary>Business Wire investor-relations / newsroom platform.</summary>
    [Display(Name = "Business Wire")]
    BusinessWire = 3,

    /// <summary>Notified (GlobeNewswire) investor-relations platform.</summary>
    [Display(Name = "Notified")]
    Notified = 4,

    /// <summary>An IR page that matched no known vendor signature — a bespoke site.</summary>
    [Display(Name = "Custom")]
    Custom = 5,
}
