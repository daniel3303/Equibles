using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// Independent of SEC registration. The optional bridge leaves legacy readers untouched.
[Index(nameof(CommonStockId), IsUnique = true)]
public class EquityIssuer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(256)]
    public string Name { get; set; }
    public Guid? CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [Required, MaxLength(2000)]
    public string IdentitySourceUrl { get; set; }
}
