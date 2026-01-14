using System.ComponentModel.DataAnnotations;

namespace Equibles.InsiderTrading.Data.Models;

public enum AcquiredDisposed {
    [Display(Name = "Acquired")] Acquired,
    [Display(Name = "Disposed")] Disposed
}
