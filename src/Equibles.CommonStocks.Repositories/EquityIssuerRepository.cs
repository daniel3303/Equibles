using System.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Equibles.CommonStocks.Repositories;

public class EquityIssuerRepository : BaseRepository<EquityIssuer>
{
    private const string CusipIdentityWriteLockSql =
        "SELECT pg_advisory_xact_lock(1163282519, 7456)";

    public EquityIssuerRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// The live stock directory. Historical identities stay stored but do not leak into search,
    /// screeners, workers, or current ticker resolution.
    /// </summary>
    public virtual IQueryable<EquityIssuer> GetCurrentUsDirectory() =>
        GetAll()
            .Where(issuer =>
                issuer.Presentation != null
                && issuer.Presentation.Listing.MarketCountryCode == "US"
                && issuer.Presentation.Listing.Active
            );

    public IQueryable<EquityIssuerSourceIdentifier> GetSourceIdentifiers() =>
        DbContext.Set<EquityIssuerSourceIdentifier>();

    public void AddSourceIdentifier(EquityIssuerSourceIdentifier identifier) =>
        DbContext.Set<EquityIssuerSourceIdentifier>().Add(identifier);

    public IQueryable<EquitySecurity> GetSecurities() =>
        GetAll().SelectMany(issuer => issuer.Securities);

    public override IQueryable<EquityIssuer> GetAll() =>
        base.GetAll()
            .Include(issuer => issuer.Presentation)
                .ThenInclude(presentation => presentation.Listing)
                    .ThenInclude(listing => listing.Security)
            .Include(issuer => issuer.Securities)
                .ThenInclude(security => security.Listings);

    /// <summary>
    /// Serializes the rare cross-table CUSIP identity writes. PostgreSQL cannot express one
    /// unique constraint across CommonStock, CommonStockCusipAlias and CommonStockListedCusip,
    /// so every writer takes this transaction-scoped database lock before checking ownership.
    /// Returns the transaction when this call created it; callers commit only that transaction.
    /// </summary>
    public async Task<IDbContextTransaction> BeginCusipIdentityWrite(
        CancellationToken cancellationToken = default
    )
    {
        if (!DbContext.Database.IsRelational())
        {
            return null;
        }

        IDbContextTransaction ownedTransaction = null;
        if (DbContext.Database.CurrentTransaction == null)
        {
            ownedTransaction = await DbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
        }

        await DbContext.Database.ExecuteSqlRawAsync(CusipIdentityWriteLockSql, cancellationToken);
        return ownedTransaction;
    }

    public async Task<IDbContextTransaction> BeginDirectoryIdentityWrite(
        CancellationToken cancellationToken = default
    )
    {
        if (DbContext == null || !DbContext.Database.IsRelational())
            return null;
        var owned =
            DbContext.Database.CurrentTransaction == null
                ? await DbContext.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken
                )
                : null;
        await DbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(1163282519, 7457)",
            cancellationToken
        );
        return owned;
    }

    public Task LockIssuerForDirectoryWrite(
        Guid issuerId,
        CancellationToken cancellationToken = default
    ) =>
        DbContext == null || !DbContext.Database.IsRelational()
            ? Task.CompletedTask
            : DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"EquityIssuer\" WHERE \"Id\" = {issuerId} FOR UPDATE",
                cancellationToken
            );

    public virtual async Task<EquityIssuer> GetCurrentUsDirectoryIssuer(params object[] key)
    {
        EquityIssuer stock = await Get(key);
        return stock?.Presentation?.Listing is { MarketCountryCode: "US", Active: true }
            ? stock
            : null;
    }

    public override Task<EquityIssuer> Get(params object[] key) =>
        GetAll().FirstOrDefaultAsync(issuer => issuer.Id == (Guid)key.Single());

    public IQueryable<EquityIssuer> GetByIds(IEnumerable<Guid> ids) =>
        GetAll().Where(stock => ids.Contains(stock.Id));

    /// <param name="includeInactive">
    /// When true, retained delisted identities (<c>Active == false</c>) are searched too —
    /// for operator surfaces that must audit what a delisting sync retired. Reader-facing
    /// surfaces keep the default active-only universe.
    /// </param>
    public IQueryable<EquityIssuer> Search(string search, bool includeInactive = false)
    {
        var query = includeInactive ? GetAll() : GetCurrentUsDirectory();

        if (string.IsNullOrEmpty(search))
        {
            return query.OrderBy(c => c.Presentation.Listing.Ticker);
        }

        foreach (var word in search.Split(" "))
        {
            // Escape LIKE metacharacters so a typed '_' or '%' matches literally
            // rather than behaving as a wildcard.
            var pattern = LikePattern.Contains(word);
            query = query.Where(c =>
                EF.Functions.ILike(c.Presentation.Listing.Ticker, pattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(c.Name, pattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(c.Description, pattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(c.Industry.Name, pattern, LikePattern.EscapeChar)
            );
        }

        // Rank an exact ticker hit to the very top, then ticker prefix hits, ahead of the
        // alphabetical fallback — so a typed symbol (e.g. "ARE") leads its group instead of
        // sorting past a per-group result cap alphabetically (ARE would otherwise fall behind
        // ABXXF/ACHC/…). Both comparisons run through ILike so they stay case-insensitive and
        // metacharacter-safe like the filter above; ordering by the boolean puts matches first.
        var exact = LikePattern.Escape(search.Trim());
        var prefix = LikePattern.StartsWith(search.Trim());
        return query
            .OrderByDescending(c =>
                EF.Functions.ILike(c.Presentation.Listing.Ticker, exact, LikePattern.EscapeChar)
            )
            .ThenByDescending(c =>
                EF.Functions.ILike(c.Presentation.Listing.Ticker, prefix, LikePattern.EscapeChar)
            )
            .ThenBy(c => c.Presentation.Listing.Ticker);
    }

    public async Task<EquityIssuer> GetByCik(string cik)
    {
        return await GetAll().FirstOrDefaultAsync(cs => cs.Cik == cik);
    }

    public async Task<EquityIssuer> GetByName(string name)
    {
        return await GetCurrentUsDirectory()
            .FirstOrDefaultAsync(cs => cs.Name.ToLower() == name.ToLower());
    }

    public IQueryable<EquityIssuer> GetByCiks(IEnumerable<string> ciks)
    {
        return GetAll().Where(cs => ciks.Contains(cs.Cik));
    }

    /// <summary>
    /// Returns the stock whose primary <c>Cik</c> equals <paramref name="cik"/>, or whose
    /// <c>SecondaryCiks</c> contains it. Primary matches are preferred so lookups remain
    /// deterministic when a subsidiary CIK is attached to a parent that's also queryable
    /// by its own primary CIK.
    /// </summary>
    public async Task<EquityIssuer> GetByAnyCik(string cik)
    {
        return await GetAll()
            .Where(cs => cs.Cik == cik || cs.SecondaryCiks.Contains(cik))
            .OrderBy(cs => cs.Cik == cik ? 0 : 1)
            .FirstOrDefaultAsync();
    }

    public IQueryable<string> GetAllSecondaryCiks()
    {
        return GetAll().Where(cs => cs.SecondaryCiks.Count > 0).SelectMany(cs => cs.SecondaryCiks);
    }

    /// <summary>
    /// Returns the stock whose primary <c>Ticker</c> equals <paramref name="ticker"/>, or whose
    /// <c>SecondaryTickers</c> contains it. A single ticker symbol can legitimately appear on
    /// more than one company (e.g. a preferred-share ticker listed under both the parent REIT
    /// and its operating-partnership SEC filer); when that happens, a primary match is returned
    /// in preference to a secondary match so lookups remain deterministic.
    /// </summary>
    public async Task<EquityIssuer> GetUsByTicker(string ticker)
    {
        var matches = GetCurrentUsDirectory()
            .Where(issuer =>
                issuer.Securities.Any(security =>
                    security.Listings.Any(listing =>
                        listing.MarketCountryCode == "US"
                        && listing.Active
                        && listing.Ticker == ticker
                        && (listing.IsDirectoryListed || listing.IsReferenceListed)
                    )
                )
            );
        var primary = await matches
            .Where(issuer =>
                issuer.Presentation.Listing.Ticker == ticker
                && issuer.Presentation.Listing.MarketCountryCode == "US"
            )
            .Take(2)
            .ToListAsync();
        if (primary.Count != 0)
            return primary.Count == 1 ? primary[0] : null;
        var owners = await matches.Take(2).ToListAsync();
        return owners.Count == 1 ? owners[0] : null;
    }

    /// <summary>
    /// Returns the stock whose primary <c>Ticker</c> equals <paramref name="ticker"/>.
    /// Primary tickers are globally unique, so at most one row can match. Use this when the
    /// caller needs to enforce primary-ticker uniqueness rather than the more permissive
    /// primary-or-secondary lookup provided by <see cref="GetByTicker"/>.
    /// </summary>
    public async Task<EquityIssuer> GetPrimaryUsByTicker(string ticker)
    {
        return await GetCurrentUsDirectory()
            .FirstOrDefaultAsync(cs => cs.Presentation.Listing.Ticker == ticker);
    }

    public IQueryable<EquityListing> GetCompletedPriceSeries() =>
        DbContext
            .Set<EquityListing>()
            .Where(listing => listing.MarketCountryCode == "US" && listing.PriceHistoryBackfilled);

    public virtual Task<Guid?> GetEquityListingId(Guid issuerId, string ticker) =>
        ResolveListingIdentity(issuerId, ticker, includeRecordedSymbols: false);

    public virtual Task<Guid?> GetRecordedEquityListingId(Guid issuerId, string ticker) =>
        ResolveListingIdentity(issuerId, ticker, includeRecordedSymbols: true);

    private async Task<Guid?> ResolveListingIdentity(
        Guid issuerId,
        string ticker,
        bool includeRecordedSymbols
    )
    {
        if (DbContext == null)
            return null;
        var ids = await DbContext
            .Set<EquityListing>()
            .Where(listing =>
                listing.Security.EquityIssuerId == issuerId
                && listing.MarketCountryCode == "US"
                && (
                    listing.Ticker == ticker
                    || includeRecordedSymbols
                        && listing.TickerAliases.Any(alias => alias.Ticker == ticker)
                )
            )
            .Select(listing => listing.Id)
            .Take(2)
            .ToListAsync();
        return ids.Count == 1 ? ids[0] : null;
    }

    public IQueryable<EquityIssuer> GetByLegalEntityIdentifier(string lei) =>
        GetAll().Where(issuer => issuer.LegalEntityIdentifier == lei);

    public IQueryable<EquityIssuer> GetUsByTickers(IEnumerable<string> tickers) =>
        GetCurrentUsDirectory()
            .Where(issuer =>
                issuer.Securities.Any(security =>
                    security.Listings.Any(listing =>
                        listing.MarketCountryCode == "US"
                        && (listing.IsDirectoryListed || listing.IsReferenceListed)
                        && tickers.Contains(listing.Ticker)
                    )
                )
            );

    public IQueryable<EquityIssuer> GetCurrentUsDirectoryByIds(IEnumerable<Guid> ids)
    {
        return GetCurrentUsDirectory().Where(cs => ids.Contains(cs.Id));
    }

    /// <summary>
    /// Loads and locks one stock until the caller's current transaction completes. Writers that
    /// key data by an authoritative listed ticker use this immediately before saving, so company
    /// sync cannot change ticker ownership between validation and the write. An unchanged tracked
    /// snapshot is refreshed after the lock; pending changes are rejected rather than discarded.
    /// </summary>
    public Task<EquityIssuer> GetForUpdate(
        Guid commonStockId,
        CancellationToken cancellationToken = default
    ) => GetWithWriteLock(commonStockId, keyUpdate: true, cancellationToken);

    /// <summary>
    /// Locks and refreshes a stock for metadata changes without blocking foreign-key inserts.
    /// Callers must not delete the stock or change foreign-key-eligible unique keys; those
    /// operations require GetForUpdate. Competing stock writers remain serialized.
    /// </summary>
    public Task<EquityIssuer> GetForNoKeyUpdate(
        Guid commonStockId,
        CancellationToken cancellationToken = default
    ) => GetWithWriteLock(commonStockId, keyUpdate: false, cancellationToken);

    private async Task<EquityIssuer> GetWithWriteLock(
        Guid commonStockId,
        bool keyUpdate,
        CancellationToken cancellationToken
    )
    {
        if (!DbContext.Database.IsRelational())
        {
            return await GetAll()
                .FirstOrDefaultAsync(stock => stock.Id == commonStockId, cancellationToken);
        }

        var operation = keyUpdate ? nameof(GetForUpdate) : nameof(GetForNoKeyUpdate);
        if (DbContext.Database.CurrentTransaction == null)
            throw new InvalidOperationException($"{operation} requires an active transaction.");

        // A tracking raw-SQL query acquires the database lock but EF identity resolution returns
        // an already-tracked instance without refreshing its values. Callers commonly preload a
        // ticker snapshot before a provider fetch, so remember that state and reload only after
        // the write lock has serialized us with a concurrent designation writer.
        var trackedEntry = DbContext
            .ChangeTracker.Entries<EquityIssuer>()
            .FirstOrDefault(entry => entry.Entity.Id == commonStockId);
        if (trackedEntry != null && trackedEntry.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException(
                $"{operation} cannot refresh CommonStock {commonStockId} while its tracked state "
                    + $"is {trackedEntry.State}; save, discard, or detach pending changes first."
            );
        }

        var pendingGraphEntry = DbContext
            .ChangeTracker.Entries()
            .FirstOrDefault(entry =>
                entry.State != EntityState.Unchanged
                && (
                    entry.Entity is EquitySecurity security
                        && security.EquityIssuerId == commonStockId
                    || entry.Entity is EquityListing listing
                        && listing.Security?.EquityIssuerId == commonStockId
                    || entry.Entity is EquityIssuerPresentation presentation
                        && presentation.EquityIssuerId == commonStockId
                )
            );
        if (pendingGraphEntry != null)
            throw new InvalidOperationException(
                $"{operation} cannot refresh a directory graph with pending {pendingGraphEntry.State} changes."
            );

        FormattableString query;
        if (keyUpdate)
            query = $"""SELECT * FROM "EquityIssuer" WHERE "Id" = {commonStockId} FOR UPDATE""";
        else
            query =
                $"""SELECT * FROM "EquityIssuer" WHERE "Id" = {commonStockId} FOR NO KEY UPDATE""";
        EquityIssuer stock = await GetDbSet()
            .FromSqlInterpolated(query)
            .Include(issuer => issuer.Presentation)
                .ThenInclude(presentation => presentation.Listing)
                    .ThenInclude(listing => listing.Security)
            .Include(issuer => issuer.Securities)
                .ThenInclude(security => security.Listings)
            .FirstOrDefaultAsync(cancellationToken);
        if (stock != null && trackedEntry != null)
        {
            await DbContext.Entry(stock).ReloadAsync(cancellationToken);
            var graph = stock
                .Securities.SelectMany(security =>
                    new object[] { security }.Concat(security.Listings)
                )
                .ToList();
            if (stock.Presentation != null)
                graph.Add(stock.Presentation);
            foreach (var entity in graph)
            {
                var entry = DbContext.Entry(entity);
                if (entry.State != EntityState.Unchanged)
                    throw new InvalidOperationException(
                        $"{operation} cannot refresh a directory graph with pending changes."
                    );
                await entry.ReloadAsync(cancellationToken);
            }
        }

        return stock;
    }

    public IQueryable<string> GetUsPrimaryTickers() =>
        GetCurrentUsDirectory()
            .Where(issuer =>
                issuer.Presentation != null && issuer.Presentation.Listing.MarketCountryCode == "US"
            )
            .Select(issuer => issuer.Presentation.Listing.Ticker);

    public IQueryable<string> GetUsSecondaryTickers() =>
        GetCurrentUsDirectory()
            .SelectMany(issuer => issuer.Securities)
            .SelectMany(security => security.Listings)
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && listing.IsDirectoryListed
                && listing.Id != listing.Security.Issuer.Presentation.EquityListingId
            )
            .Select(listing => listing.Ticker);

    /// <summary>
    /// Retired CUSIPs recorded when a stock's CUSIP changed. They belong to the
    /// CommonStock aggregate (no independent lifecycle), so access lives here
    /// rather than in a dedicated repository.
    /// </summary>
    public IQueryable<EquityIssuerCusipAlias> GetCusipAliases()
    {
        return DbContext.Set<EquityIssuerCusipAlias>().AsQueryable();
    }

    public EquityIssuerCusipAlias AddCusipAlias(EquityIssuerCusipAlias alias)
    {
        DbContext.Set<EquityIssuerCusipAlias>().Add(alias);
        return alias;
    }

    public void DeleteCusipAlias(EquityIssuerCusipAlias alias)
    {
        DbContext.Set<EquityIssuerCusipAlias>().Remove(alias);
    }

    /// <summary>
    /// CUSIPs of a filer's OTHER listed securities (sibling share classes, units),
    /// keyed to the exact secondary ticker they identify. Same aggregate reasoning
    /// as the CUSIP aliases: no independent lifecycle, so access lives here.
    /// </summary>
    public IQueryable<EquityListingCusipEvidence> GetListedCusips()
    {
        return DbContext.Set<EquityListingCusipEvidence>().AsQueryable();
    }

    public EquityListingCusipEvidence AddListedCusip(EquityListingCusipEvidence listedCusip)
    {
        DbContext.Set<EquityListingCusipEvidence>().Add(listedCusip);
        return listedCusip;
    }

    public void DeleteListedCusip(EquityListingCusipEvidence listedCusip)
    {
        DbContext.Set<EquityListingCusipEvidence>().Remove(listedCusip);
    }

    public virtual IQueryable<EquityListingRetirementEvidence> GetDelistedListings()
    {
        return DbContext.Set<EquityListingRetirementEvidence>().AsQueryable();
    }

    public EquityListingRetirementEvidence AddDelistedListing(
        EquityListingRetirementEvidence delistedListing
    )
    {
        DbContext.Set<EquityListingRetirementEvidence>().Add(delistedListing);
        return delistedListing;
    }

    public void DeleteDelistedListing(EquityListingRetirementEvidence delistedListing)
    {
        DbContext.Set<EquityListingRetirementEvidence>().Remove(delistedListing);
    }

    public async Task<EquityListingRetirementEvidence> GetDelistedListingForUpdate(
        Guid delistedListingId,
        CancellationToken cancellationToken = default
    )
    {
        var listings = DbContext.Set<EquityListingRetirementEvidence>();
        if (!DbContext.Database.IsRelational())
        {
            return await listings.FirstOrDefaultAsync(
                listing => listing.Id == delistedListingId,
                cancellationToken
            );
        }

        if (DbContext.Database.CurrentTransaction == null)
            throw new InvalidOperationException(
                "GetDelistedListingForUpdate requires an active transaction."
            );

        var trackedEntry = DbContext
            .ChangeTracker.Entries<EquityListingRetirementEvidence>()
            .FirstOrDefault(entry => entry.Entity.Id == delistedListingId);
        if (trackedEntry != null && trackedEntry.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException(
                $"GetDelistedListingForUpdate cannot refresh listing {delistedListingId} while "
                    + $"its tracked state is {trackedEntry.State}."
            );
        }

        var listing = await listings
            .FromSqlInterpolated(
                $"""SELECT * FROM "CommonStockDelistedListing" WHERE "Id" = {delistedListingId} FOR UPDATE"""
            )
            .FirstOrDefaultAsync(cancellationToken);
        if (listing != null && trackedEntry != null)
            await DbContext.Entry(listing).ReloadAsync(cancellationToken);

        return listing;
    }

    /// <summary>
    /// Retired primary tickers recorded when the SEC sync renamed a stock's symbol.
    /// Same aggregate reasoning as the CUSIP aliases: no independent lifecycle, so
    /// access lives here rather than in a dedicated repository. Consulted only on
    /// the miss path — a live ticker always resolves before any alias is looked at.
    /// </summary>
    public IQueryable<EquityIssuerTickerAlias> GetTickerAliases()
    {
        return DbContext.Set<EquityIssuerTickerAlias>().AsQueryable();
    }

    public EquityIssuerTickerAlias AddTickerAlias(EquityIssuerTickerAlias alias)
    {
        DbContext.Set<EquityIssuerTickerAlias>().Add(alias);
        return alias;
    }

    public void DeleteTickerAlias(EquityIssuerTickerAlias alias)
    {
        DbContext.Set<EquityIssuerTickerAlias>().Remove(alias);
    }
}
