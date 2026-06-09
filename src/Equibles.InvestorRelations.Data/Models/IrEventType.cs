using System.ComponentModel.DataAnnotations;

namespace Equibles.InvestorRelations.Data.Models;

public enum IrEventType
{
    [Display(Name = "Other")]
    Other,

    [Display(Name = "Earnings Call")]
    EarningsCall,

    [Display(Name = "Conference")]
    Conference,

    [Display(Name = "Webcast")]
    Webcast,

    [Display(Name = "Shareholder Meeting")]
    ShareholderMeeting,

    [Display(Name = "Presentation")]
    Presentation,
}
