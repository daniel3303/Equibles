using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class EquityListingRepository : BaseRepository<EquityListing>
{
    public EquityListingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    // The caller supplies source-validated USD evidence; the database revalidates exact
    // current U.S. ownership under the same lock used by directory writers.
    public async Task<bool> RecordUsDollarQuotation(
        Guid issuerId,
        string ticker,
        string source,
        string payloadJson,
        CancellationToken cancellationToken = default,
        Guid? expectedListingId = null
    )
    {
        if (DbContext.Database.CurrentTransaction != null)
            throw new InvalidOperationException(
                "Quotation capture requires an independent transaction."
            );
        await using var transaction = await DbContext.Database.BeginTransactionAsync(
            cancellationToken
        );
        await DbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(1163282519, 7457)",
            cancellationToken
        );
        var candidates = await GetUsByTicker(ticker)
            .AsNoTracking()
            .Select(row => new
            {
                row.Id,
                row.Security.EquityIssuerId,
                row.TradingCurrency,
                row.QuoteUnitMultiplier,
            })
            .Take(2)
            .ToListAsync(cancellationToken);
        if (
            candidates.Count != 1
            || candidates[0].EquityIssuerId != issuerId
            || expectedListingId.HasValue && candidates[0].Id != expectedListingId.Value
        )
            return false;
        var listing = candidates[0];
        if (
            listing.TradingCurrency is not (null or "USD")
            || listing.QuoteUnitMultiplier is not (null or 1m)
        )
            return false;
        await EquityDirectorySourceRecordRepository.Append(
            DbContext,
            source,
            listing.Id.ToString(),
            payloadJson,
            cancellationToken
        );
        var updated = await GetUsByTicker(ticker)
            .Where(row =>
                row.Id == listing.Id
                && row.Security.EquityIssuerId == issuerId
                && (row.TradingCurrency == null || row.TradingCurrency == "USD")
                && (row.QuoteUnitMultiplier == null || row.QuoteUnitMultiplier == 1m)
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(row => row.TradingCurrency, "USD")
                        .SetProperty(row => row.QuoteUnitMultiplier, 1m),
                cancellationToken
            );
        if (updated != 1)
            return false;
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public IQueryable<EquityListing> GetByLegacyKey(Guid stockId, string ticker) =>
        DbContext
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == stockId && row.ListedTicker == ticker)
            .Select(row => row.Listing);

    public IQueryable<EquityListing> GetByRecordedUsSymbol(Guid issuerId, string ticker) =>
        GetAll()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && listing.Security.EquityIssuerId == issuerId
                && (
                    listing.Ticker == ticker
                    || listing.TickerAliases.Any(alias => alias.Ticker == ticker)
                )
            );

    public IQueryable<EquityListingSymbolReference> GetRecordedUsSymbols(
        IEnumerable<Guid> issuerIds
    )
    {
        var listings = GetAll()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && issuerIds.Contains(listing.Security.EquityIssuerId)
            );
        return listings
            .Select(listing => new EquityListingSymbolReference
            {
                EquityIssuerId = listing.Security.EquityIssuerId,
                EquityListingId = listing.Id,
                Ticker = listing.Ticker,
            })
            .Union(
                listings
                    .SelectMany(listing => listing.TickerAliases)
                    .Select(alias => new EquityListingSymbolReference
                    {
                        EquityIssuerId = alias.Listing.Security.EquityIssuerId,
                        EquityListingId = alias.EquityListingId,
                        Ticker = alias.Ticker,
                    })
            );
    }

    public IQueryable<EquityListing> GetVerifiedByMarket(string mic, string ticker) =>
        GetAll()
            .Where(row =>
                row.Active
                && row.IdentityState == EquityIdentityState.Verified
                && row.MarketIdentifierCode == mic
                && row.Ticker == ticker
            );

    public IQueryable<EquityListing> GetActiveUsListings() =>
        GetAll().Where(listing => listing.Active && listing.MarketCountryCode == "US");

    public IQueryable<EquityListing> GetUsByTicker(string ticker) =>
        GetActiveUsListings().Where(listing => listing.Ticker == ticker);
}
