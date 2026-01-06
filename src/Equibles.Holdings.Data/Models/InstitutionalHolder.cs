using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Index(nameof(Cik), IsUnique = true)]
[Index(nameof(Name))]
public class InstitutionalHolder {
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(16)]
    public string Cik { get; set; }

    [MaxLength(512)]
    public string Name { get; set; }

    [MaxLength(128)]
    public string City { get; set; }

    [MaxLength(64)]
    public string StateOrCountry { get; set; }

    [MaxLength(32)]
    public string Form13FFileNumber { get; set; }

    [MaxLength(32)]
    public string CrdNumber { get; set; }

    public virtual List<InstitutionalHolding> Holdings { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
