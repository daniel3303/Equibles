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

    // Every live share line FIRDS holds for one ISIN, across registers, for home decisions that weigh its other venues.
    public Task<List<FirdsInstrumentRecord>> GetLiveShareLines(
        string isin,
        DateTime asOf,
        CancellationToken cancellationToken = default
    ) => GetLiveShares(asOf).Where(row => row.Isin == isin).ToListAsync(cancellationToken);

    // Live shares homed on the market: by relevant venue, or by the issuer's own request where FIRDS names a relevant
    // venue the issuer never sought (EquityMarketDirectoryGate.IsHomedByIssuerRequest, by the same conditions).
    public Task<int> CountHomeShares(
        EquityMarket market,
        DateTime asOf,
        CancellationToken cancellationToken = default
    ) => GetHomeShareIsins(market, asOf).CountAsync(cancellationToken);

    public IQueryable<string> GetHomeShareIsins(EquityMarket market, DateTime asOf)
    {
        var homeVenues = market.HomeVenueCodes;
        var firdsVenues = market.FirdsVenueCodes;
        var outsideFirds = EquityMarketCatalog.IssuingCountriesHomedOutsideFirds;
        var live = GetLiveShares(asOf).Where(row => row.Authority == market.FirdsAuthority);
        var requested = live.Where(row =>
            firdsVenues.Contains(row.Mic)
            && !outsideFirds.Contains(row.Isin.Substring(0, 2))
            && !homeVenues.Contains(row.RelevantTradingVenue)
            && live.Any(line =>
                line.Isin == row.Isin
                && firdsVenues.Contains(line.Mic)
                && line.IssuerRequested == true
            )
            && live.Any(line => line.Isin == row.Isin && line.Mic == row.RelevantTradingVenue)
            && !live.Any(line =>
                line.Isin == row.Isin
                && line.Mic == row.RelevantTradingVenue
                && line.IssuerRequested != false
            )
            && !live.Any(line =>
                line.Isin == row.Isin
                && line.IssuerRequested != false
                && !firdsVenues.Contains(line.Mic)
                && !homeVenues.Contains(line.Mic)
            )
        );
        foreach (
            var other in EquityMarketCatalog.All.Where(other =>
                other.Code != market.Code && other.FirdsAuthority != null
            )
        )
        {
            // Correlated by ISIN, so only the few candidates probe the ISIN index instead of hashing every home set.
            var homedThere = HomeLinesByRelevantVenue(other, asOf);
            requested = requested.Where(row => !homedThere.Any(line => line.Isin == row.Isin));
        }
        return HomeLinesByRelevantVenue(market, asOf)
            .Select(row => row.Isin)
            .Union(requested.Select(row => row.Isin));
    }

    // Live shares whose relevant venue is a home venue. Where the market requires it, the issuer must also have sought
    // (or not denied) a home admission, since venues elsewhere misreport the flag on their own listings.
    private IQueryable<FirdsInstrumentRecord> HomeLinesByRelevantVenue(
        EquityMarket market,
        DateTime asOf
    )
    {
        var authority = market.FirdsAuthority;
        var homeVenues = market.HomeVenueCodes;
        var live = GetLiveShares(asOf).Where(row => row.Authority == authority);
        var home = live.Where(row => homeVenues.Contains(row.RelevantTradingVenue));
        if (market.HomeRequiresIssuerRequest)
            home = home.Where(row =>
                live.Any(line =>
                    line.Isin == row.Isin
                    && homeVenues.Contains(line.Mic)
                    && line.IssuerRequested != false
                )
            );
        return home;
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
                        IssuerRequested = fresh.IssuerRequested,
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
