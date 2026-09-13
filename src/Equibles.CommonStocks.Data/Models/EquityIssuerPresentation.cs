using System.ComponentModel.DataAnnotations;

namespace Equibles.CommonStocks.Data.Models;

// The default listing for issuer-only requests; explicit listing requests keep their identity.
public class EquityIssuerPresentation
{
    [Key]
    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }
    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }
}
