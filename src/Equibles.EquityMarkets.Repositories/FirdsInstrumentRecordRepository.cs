using Equibles.Data;
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

    // The gate a directory row must pass: a live share whose most relevant venue is this MIC.
    public Task<FirdsInstrumentRecord> GetPrimaryVenueShare(
        string isin,
        string mic,
        DateTime asOf,
        CancellationToken cancellationToken = default
    ) =>
        GetLiveShares(asOf)
            .Where(row => row.Isin == isin && row.Mic == mic && row.RelevantTradingVenue == mic)
            .OrderBy(row => row.Authority)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountPrimaryVenueShares(
        IEnumerable<string> mics,
        DateTime asOf,
        CancellationToken cancellationToken = default
    ) =>
        GetLiveShares(asOf)
            .Where(row => mics.Contains(row.Mic) && row.RelevantTradingVenue == row.Mic)
            .Select(row => row.Isin)
            .Distinct()
            .CountAsync(cancellationToken);

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
                        RemovedAt = null,
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
