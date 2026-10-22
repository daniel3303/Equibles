using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class SecTickerDirectoryObservationRepository : BaseRepository<SecTickerDirectoryObservation>
{
    public SecTickerDirectoryObservationRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// The spans the newest capture listed, i.e. what the directory states today. Every capture
    /// extends each span it lists to its own date, so the newest date marks them all.
    /// </summary>
    public IQueryable<SecTickerDirectoryObservation> GetCurrent() =>
        GetAll()
            .Where(observation =>
                observation.LastObservedOn
                == GetAll().Max(latest => (DateOnly?)latest.LastObservedOn)
            );

    public IQueryable<SecTickerDirectoryObservation> GetByTickers(IEnumerable<string> tickers) =>
        GetAll().Where(observation => tickers.Contains(observation.Ticker));
}
