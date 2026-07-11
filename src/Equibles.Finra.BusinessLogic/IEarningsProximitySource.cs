namespace Equibles.Finra.BusinessLogic;

/// <summary>
/// Optional data source for the squeeze score's earnings-proximity catalyst:
/// which stocks currently sit inside the window around a scheduled earnings
/// event where squeezes cluster (from
/// <see cref="ShortSqueezeScoreManager.EarningsProximityLeadWeekdays"/> weekdays
/// before the event to <see cref="ShortSqueezeScoreManager.EarningsProximityTrailWeekdays"/>
/// weekdays after it). The open-source build ships no implementation — earnings
/// calendars are not part of the public dataset — so the catalyst simply never
/// fires; a host that knows earnings dates registers an implementation and every
/// squeeze surface picks the boost up through the manager.
/// </summary>
public interface IEarningsProximitySource
{
    /// <summary>
    /// Of <paramref name="stockIds"/>, the stocks currently inside the earnings
    /// window. A stock the source knows nothing about is simply absent — absence
    /// of calendar data means no catalyst, never an error.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetStocksNearEarnings(
        IReadOnlyCollection<Guid> stockIds,
        CancellationToken cancellationToken
    );
}
