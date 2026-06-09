using System.ComponentModel.DataAnnotations;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// Kind of investor-relations event, normalised across the various labels the
/// source platforms use. <see cref="Unknown"/> when the scraper cannot map the
/// source label to a known kind.
/// </summary>
public enum IrEventType
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "Earnings call")]
    EarningsCall = 1,

    [Display(Name = "Conference")]
    Conference = 2,

    [Display(Name = "Presentation")]
    Presentation = 3,

    [Display(Name = "Shareholder meeting")]
    ShareholderMeeting = 4,

    [Display(Name = "Webcast")]
    Webcast = 5,

    [Display(Name = "Other")]
    Other = 6,
}
