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

    [MaxLength(9)]
    public string Cusip { get; set; }

    // Preserve the filing's broad registration class independently of precise instrument form.
    public ListedSecurityType RegistrationType { get; set; }

    [MaxLength(500)]
    public string RegistrationTitle { get; set; }

    public long SharesOutstanding { get; set; }

    // Stored as the provider reports it, in major units of the presentation listing's trading currency
    // (USD for a US listing, EUR on Euronext, GBP on London); rank-only readers tolerate the mix, sums may not.
    public double MarketCapitalization { get; set; }
    public virtual List<EquityListing> Listings { get; set; } = [];

    [MaxLength(2000)]
    public string IdentitySourceUrl { get; set; }
}
