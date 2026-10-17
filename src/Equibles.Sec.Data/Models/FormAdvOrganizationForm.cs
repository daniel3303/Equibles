using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>Form ADV Part 1A Item 3.A, how the adviser is organized.</summary>
public enum FormAdvOrganizationForm
{
    [Display(Name = "Corporation")]
    Corporation = 0,

    [Display(Name = "Sole proprietorship")]
    SoleProprietorship = 1,

    [Display(Name = "Limited liability partnership")]
    LimitedLiabilityPartnership = 2,

    [Display(Name = "Partnership")]
    Partnership = 3,

    [Display(Name = "Limited liability company")]
    LimitedLiabilityCompany = 4,

    [Display(Name = "Limited partnership")]
    LimitedPartnership = 5,

    [Display(Name = "Other")]
    Other = 6,
}
