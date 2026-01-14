using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data.Models;

[Index(nameof(OwnerCik), IsUnique = true)]
[Index(nameof(Name))]
public class InsiderOwner {
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(16)]
    public string OwnerCik { get; set; }

    [MaxLength(512)]
    public string Name { get; set; }

    [MaxLength(128)]
    public string City { get; set; }

    [MaxLength(64)]
    public string StateOrCountry { get; set; }

    public bool IsDirector { get; set; }
    public bool IsOfficer { get; set; }

    [MaxLength(128)]
    public string OfficerTitle { get; set; }

    public bool IsTenPercentOwner { get; set; }

    public virtual List<InsiderTransaction> Transactions { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
