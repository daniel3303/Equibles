using System.ComponentModel.DataAnnotations;

namespace Equibles.FdaCatalysts.Data.Models;

public enum FdaCatalystType
{
    [Display(Name = "Advisory Committee Meeting")]
    AdvisoryCommittee,

    [Display(Name = "PDUFA Decision")]
    Pdufa,

    [Display(Name = "Complete Response Follow-up")]
    CompleteResponse,
}
