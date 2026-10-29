using Equibles.CommonStocks.Data.Models;

namespace Equibles.Yahoo.Data.Prices;

// A venue lane that writes a listed security's share count and market capitalization from the
// exchange's own figures owns that pair, as a venue bar owns its day; Yahoo's key statistics never
// overwrite a security an owner claims. With no owner registered, Yahoo keeps the pair.
public interface IVenueCapitalizationOwner
{
    Task<bool> Owns(EquitySecurity security, CancellationToken cancellationToken);
}
