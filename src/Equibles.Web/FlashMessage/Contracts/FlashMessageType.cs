using System.ComponentModel.DataAnnotations;

namespace Equibles.Web.FlashMessage.Contracts;

public enum FlashMessageType {
    [Display(Name = "Information")]
    Info,

    [Display(Name = "Warning")]
    Warning,

    [Display(Name = "Error")]
    Error,

    [Display(Name = "Success")]
    Success
}
