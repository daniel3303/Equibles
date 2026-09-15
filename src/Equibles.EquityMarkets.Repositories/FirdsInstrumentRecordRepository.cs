using Equibles.Data;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.EquityMarkets.Repositories;

public class FirdsInstrumentRecordRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<FirdsInstrumentRecord>(dbContext)
{
    public IQueryable<FirdsInstrumentRecord> GetLive(DateTime asOf) =>
        GetAll()
            .Where(row =>
                row.RemovedAt == null && (row.TerminationDate == null || row.TerminationDate > asOf)
            );

    // Ordinary and preference shares; receipts, convertibles, units and structured products are not stocks.
    public IQueryable<FirdsInstrumentRecord> GetLiveShares(DateTime asOf) =>
        GetLive(asOf).Where(row => row.Cfi.StartsWith("ES") || row.Cfi.StartsWith("EP"));

    // The universe check a directory row must pass: a live share line the market's authority records on one of
    // its venues, the line filed on the relevant venue itself first when there is one.
    public Task<FirdsInstrumentRecord> GetLiveShare(
        string isin,
        string authority,
        IReadOnlyList<string> venueCodes,
        DateTime asOf,
        CancellationToken cancellationToken = default
    ) =>
        GetLiveShares(asOf)
            .Where(row =>
                row.Authority == authority && row.Isin == isin && venueCodes.Contains(row.Mic)
            )
            .OrderBy(row => row.Mic == row.RelevantTradingVenue ? 0 : 1)
            .ThenBy(row => row.Mic)
            .FirstOrDefaultAsync(cancellationToken);

    // Live shares whose relevant venue is one of the market's home venues; Frankfurt's Freiverkehr is also the first
    // EU admission of many foreign shares, so for Xetra this counts more than its directory names as home.
    public Task<int> CountHomeShares(
        EquityMarket market,
        DateTime asOf,
        CancellationToken cancellationToken = default
    )
    {
        var authority = market.FirdsAuthority;
        var homeVenues = market.HomeVenueCodes;
        return GetLiveShares(asOf)
            .Where(row =>
                row.Authority == authority && homeVenues.Contains(row.RelevantTradingVenue)
            )
            .Select(row => row.Isin)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public Task UpsertRange(
        IEnumerable<FirdsInstrumentRecord> rows,
        CancellationToken cancellationToken = default
    ) =>
        GetDbSet()
            .UpsertRange(rows)
            .On(row => new
            {
                row.Authority,
                row.Isin,
                row.Mic,
            })
            .WhenMatched(
                (existing, fresh) =>
                    new FirdsInstrumentRecord
                    {
                        Lei = fresh.Lei,
                        Cfi = fresh.Cfi,
                        Currency = fresh.Currency,
                        FullName = fresh.FullName,
                        ShortName = fresh.ShortName,
                        FirstTradeDate = fresh.FirstTradeDate,
                        TerminationDate = fresh.TerminationDate,
                        RelevantCompetentAuthority = fresh.RelevantCompetentAuthority,
                        RelevantTradingVenue = fresh.RelevantTradingVenue,
                        ObservedAt = fresh.ObservedAt,
                        RemovedAt = fresh.RemovedAt,
                    }
            )
            .RunAsync(cancellationToken);

    // After a complete full file: every row of the authority the file did not restate is gone.
    public Task<int> MarkRemovedBefore(
        string authority,
        DateTime observedBefore,
        DateTime removedAt,
        CancellationToken cancellationToken = default
    ) =>
        GetAll()
            .Where(row =>
                row.Authority == authority
                && row.RemovedAt == null
                && row.ObservedAt < observedBefore
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.RemovedAt, removedAt),
                cancellationToken
            );
}
