using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// One share class or receipt; multiple venues may trade this same security.
[Index(nameof(Isin), IsUnique = true)]
public class EquitySecurity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }

    [MaxLength(12)]
    public string Isin { get; set; }
    public EquitySecurityKind SecurityType { get; set; }

    [MaxLength(2000)]
    public string IdentitySourceUrl { get; set; }
}
