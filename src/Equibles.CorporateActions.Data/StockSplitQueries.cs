using Equibles.CorporateActions.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// The one way to load captured splits for restatement. <see cref="SplitBasisResolver"/> and
/// <see cref="PriceSeriesSplitScope"/> read <see cref="StockSplit.Listing"/> on every split dated
/// after the as-of date, and the rows are routinely resolved after the loading scope is gone, so
/// the listing has to travel with the row: a lazy load on a disposed context throws and, in the
/// 13F import, skips the whole filing.
/// </summary>
public static class StockSplitQueries
{
    public static IQueryable<StockSplit> ForIssuers(
        DbContext dbContext,
        IEnumerable<Guid> issuerIds
    )
    {
        var ids = issuerIds as ICollection<Guid> ?? issuerIds.ToList();
        return dbContext
            .Set<StockSplit>()
            .Include(split => split.Listing)
            .Where(split => ids.Contains(split.EquityIssuerId));
    }
}
