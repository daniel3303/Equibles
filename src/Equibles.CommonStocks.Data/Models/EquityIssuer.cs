using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// Independent of SEC registration. The optional bridge leaves legacy readers untouched.
[Index(nameof(CommonStockId), IsUnique = true)]
[Index(nameof(LegalEntityIdentifier), IsUnique = true)]
public class EquityIssuer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Name { get; set; }

    [MaxLength(2000)]
    public string Description { get; set; }

    [MaxLength(16)]
    public string Cik { get; set; }

    [MaxLength(20)]
    public string LegalEntityIdentifier { get; set; }

    public List<string> SecondaryCiks
    {
        get => field ?? [];
        set;
    } = [];

    [MaxLength(256)]
    public string Website { get; set; }

    public DateTime? WebsiteCheckedAt { get; set; }
    public int? FiscalYearEndMonth { get; set; }
    public int? FiscalYearEndDay { get; set; }

    [MaxLength(8)]
    public string Sic { get; set; }

    [MaxLength(32)]
    public string EntityType { get; set; }

    public Guid? IndustryId { get; set; }
    public virtual Industry Industry { get; set; }
    public virtual List<EquitySecurity> Securities { get; set; } = [];
    public virtual EquityIssuerPresentation Presentation { get; set; }

    public Guid? CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [MaxLength(2000)]
    public string IdentitySourceUrl { get; set; }
}
