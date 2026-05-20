using System.ComponentModel.DataAnnotations;

namespace Equibles.Messaging.Contracts.Activity;

public enum ScraperActivitySeverity
{
    [Display(Name = "Info")]
    Info = 0,

    [Display(Name = "Warning")]
    Warn = 1,

    [Display(Name = "Error")]
    Error = 2,
}
