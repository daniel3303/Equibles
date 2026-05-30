using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A firm named on a <see cref="NCenFiling"/> as serving the registered investment company in a
/// particular role (see <see cref="NCenServiceProviderType"/>) — for example its investment
/// adviser, custodian, transfer agent or independent auditor. N-CEN carries the provider's name and
/// country but no resolvable identifier we track, so the name is stored as free text.
/// </summary>
[Index(nameof(NCenFilingId))]
public class NCenServiceProvider
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid NCenFilingId { get; set; }
    public virtual NCenFiling NCenFiling { get; set; }

    /// <summary>The role this firm plays for the fund.</summary>
    public NCenServiceProviderType ProviderType { get; set; }

    /// <summary>The firm's name exactly as reported on the filing.</summary>
    [MaxLength(512)]
    public string Name { get; set; }

    /// <summary>The firm's country code when reported, e.g. "US".</summary>
    [MaxLength(8)]
    public string Country { get; set; }

    /// <summary>True when the filing reports the firm as affiliated with the registrant.</summary>
    public bool IsAffiliated { get; set; }
}
