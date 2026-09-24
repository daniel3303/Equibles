using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The listing identity an issuer's stored position labels were last converged against, so the
/// convergence pass rescans an issuer's positions only after that identity changes.
/// </summary>
public class HoldingLabelConvergenceState
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid EquityIssuerId { get; set; }

    [Required, MaxLength(64)]
    public string Fingerprint { get; set; }

    public DateTime ConvergedAt { get; set; }
}
