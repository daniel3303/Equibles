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

    /// <summary>
    /// <see cref="ForIssuers"/> narrowed to splits already effective on <paramref name="today"/>,
    /// for figures restated onto today's basis. A split row exists from its announcement, but no
    /// count or close moves until it takes effect, so an announced split must neither restate a
    /// figure nor hold its valuation as an unreconciled basis.
    /// </summary>
    public static IQueryable<StockSplit> EffectiveForIssuers(
        DbContext dbContext,
        IEnumerable<Guid> issuerIds,
        DateOnly today
    ) => ForIssuers(dbContext, issuerIds).Where(split => split.EffectiveDate <= today);

    /// <summary>
    /// Every split already effective on <paramref name="today"/> whose price adjustment has been
    /// applied, for a pass that walks the whole split history rather than one issuer's: only
    /// those splits have moved both the stored closes and the counts restated against them.
    /// </summary>
    public static IQueryable<StockSplit> AppliedEffective(DbContext dbContext, DateOnly today) =>
        dbContext
            .Set<StockSplit>()
            .Include(split => split.Listing)
            .Where(split =>
                split.EffectiveDate <= today && split.PriceAdjustmentAppliedTime != null
            );
}
