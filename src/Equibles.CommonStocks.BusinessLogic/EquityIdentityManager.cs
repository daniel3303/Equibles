using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Exceptions;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.BusinessLogic;

public enum DelistedListingCusipSeedResult
{
    Skipped,
    Seeded,
    Ambiguous,
    ClaimedByAnotherStock,
}

[Service]
public class EquityIdentityManager
{
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly IBus _bus;

    public EquityIdentityManager(EquityIssuerRepository commonStockRepository, IBus bus)
    {
        _commonStockRepository = commonStockRepository;
        _bus = bus;
    }

    /// <summary>
    /// Finalizes an authoritative historical CUSIP for one retained listed identity.
    /// The stock row is locked before the listing row so company-sync reactivation and
    /// historical-identity finalization cannot interleave. The listing cutoff and request
    /// checkpoint are revalidated under those locks before any identity is written.
    /// </summary>
    public async Task<DelistedListingCusipSeedResult> SeedDelistedListingCusip(
        Guid listingId,
        string cusip,
        DateOnly settlementDate,
        DateTime sweepStartedAt,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedCusip = cusip?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCusip))
        {
            return DelistedListingCusipSeedResult.Skipped;
        }

        await using var transaction = await _commonStockRepository.BeginCusipIdentityWrite(
            cancellationToken
        );
        var stockId = await _commonStockRepository
            .GetDelistedListings()
            .AsNoTracking()
            .Where(listing => listing.Id == listingId)
            .Select(listing => (Guid?)listing.EquityIssuerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (stockId == null)
        {
            return DelistedListingCusipSeedResult.Skipped;
        }

        EquityIssuer stock = await _commonStockRepository.GetForUpdate(
            stockId.Value,
            cancellationToken
        );
        if (stock == null)
        {
            return DelistedListingCusipSeedResult.Skipped;
        }

        var listing = await _commonStockRepository.GetDelistedListingForUpdate(
            listingId,
            cancellationToken
        );
        if (
            listing == null
            || listing.EquityIssuerId != stock.Id
            || listing.Cusip != null
            || settlementDate > listing.DelistedOn
            || !SamePostgresTimestamp(listing.HistoricalCusipBackfillSweepStartedAt, sweepStartedAt)
            || listing.HistoricalCusipBackfillCandidateOn != settlementDate
            || listing.HistoricalCusipBackfillAmbiguous
            || listing.HistoricalCusipBackfillCandidates.Count != 1
            || !string.Equals(
                listing.HistoricalCusipBackfillCandidates[0],
                normalizedCusip,
                StringComparison.OrdinalIgnoreCase
            )
            || (
                listing.HistoricalCusipBackfillRequestedAt != null
                && listing.HistoricalCusipBackfillRequestedAt > sweepStartedAt
            )
        )
        {
            return DelistedListingCusipSeedResult.Skipped;
        }

        var primaryClaims = await _commonStockRepository
            .GetSecurities()
            .Where(security =>
                security.Cusip != null && security.Cusip.ToUpper() == normalizedCusip
            )
            .Select(security => new { security.Id, security.EquityIssuerId })
            .ToListAsync(cancellationToken);
        var aliasClaims = await _commonStockRepository
            .GetCusipAliases()
            .Where(alias => alias.Cusip.ToUpper() == normalizedCusip)
            .Select(alias => alias.EquityIssuerId)
            .ToListAsync(cancellationToken);
        var listedClaims = await _commonStockRepository
            .GetListedCusips()
            .Where(candidate => candidate.Cusip.ToUpper() == normalizedCusip)
            .Select(candidate => new { candidate.EquityIssuerId, candidate.ListedTicker })
            .ToListAsync(cancellationToken);
        if (
            primaryClaims.Any(claim => claim.EquityIssuerId != stock.Id)
            || aliasClaims.Any(ownerId => ownerId != stock.Id)
            || listedClaims.Any(candidate => candidate.EquityIssuerId != stock.Id)
        )
        {
            return DelistedListingCusipSeedResult.ClaimedByAnotherStock;
        }

        var isPrimary =
            stock.Presentation?.Listing is { MarketCountryCode: "US" }
            && string.Equals(
                listing.ListedTicker,
                stock.Presentation?.Listing?.Ticker,
                StringComparison.OrdinalIgnoreCase
            );
        var exactListedClaim = listedClaims.FirstOrDefault(candidate =>
            candidate.EquityIssuerId == stock.Id
            && string.Equals(
                candidate.ListedTicker,
                listing.ListedTicker,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (
            (
                isPrimary
                && primaryClaims.Any(claim =>
                    claim.Id != stock.Presentation.Listing.EquitySecurityId
                )
            )
            || aliasClaims.Count > 0
            || (isPrimary && listedClaims.Count > 0)
            || (!isPrimary && primaryClaims.Count > 0)
            || (!isPrimary && listedClaims.Count > 0 && exactListedClaim == null)
            || (
                isPrimary
                && stock.Presentation.Listing.Security.Cusip != null
                && !string.Equals(
                    stock.Presentation.Listing.Security.Cusip,
                    normalizedCusip,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            listing.HistoricalCusipBackfillAmbiguous = true;
            await _commonStockRepository.SaveChanges();
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return DelistedListingCusipSeedResult.Ambiguous;
        }

        var identityAdded = false;
        if (isPrimary && stock.Presentation.Listing.Security.Cusip == null)
        {
            stock.Presentation.Listing.Security.Cusip = normalizedCusip;
            identityAdded = true;
        }
        else if (!isPrimary && exactListedClaim == null)
        {
            _commonStockRepository.AddListedCusip(
                new EquityListingCusipEvidence
                {
                    EquityIssuerId = stock.Id,
                    ListedTicker = listing.ListedTicker,
                    Cusip = normalizedCusip,
                }
            );
            identityAdded = true;
        }

        listing.Cusip = normalizedCusip;
        await _commonStockRepository.SaveChanges();
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        if (identityAdded)
        {
            await _bus.Publish(
                new StockCusipChanged(
                    stock.Id,
                    stock.Presentation?.Listing?.Ticker,
                    null,
                    stock.Presentation?.Listing?.Security?.Cusip
                ),
                cancellationToken
            );
        }

        return DelistedListingCusipSeedResult.Seeded;
    }

    private static bool SamePostgresTimestamp(DateTime? persisted, DateTime expected) =>
        persisted.HasValue && persisted.Value.Ticks / 10 == expected.Ticks / 10;

    /// <summary>
    /// Records CUSIPs a stock USED to trade under, without touching its current one.
    /// <para>
    /// <see cref="SetCusip"/> only ever captures a retirement it witnesses live, so a
    /// CUSIP change that happened before this pipeline first ran leaves no alias — and
    /// every 13F line filed under the retired value stays unmappable forever. AMC's
    /// pre-2023-reverse-split 00165C104 is the shape: `GetTopHolders(AMC, 2022-12-31)`
    /// answered with 4 institutions holding 845 shares, against 274 holding 102M a year
    /// later. The cliff is the CUSIP change, not an ownership event.
    /// </para>
    /// <para>
    /// Callers must have established that the CUSIP belongs to this issuer. Aliases the
    /// table has already claimed are left with their first owner (one CUSIP identifies
    /// one security, ever), so a re-run records nothing and publishes nothing.
    /// </para>
    /// <para>
    /// Recording one publishes <see cref="StockCusipChanged"/> for the same reason
    /// <see cref="SetCusip"/> does: quarterly 13F data sets already marked processed hold
    /// no holdings for lines filed under the newly-mapped CUSIP, and the consumer clears
    /// that ledger so the Holdings worker re-imports them. A burst collapses to a no-op
    /// once cleared, so a sweep recording many aliases costs one invalidation.
    /// </para>
    /// </summary>
    public async Task<int> RecordRetiredCusipAliases(
        EquityIssuer commonStock,
        IEnumerable<string> retiredCusips
    )
    {
        ArgumentNullException.ThrowIfNull(commonStock);
        ArgumentNullException.ThrowIfNull(retiredCusips);

        var candidates = retiredCusips
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Where(c =>
                !string.Equals(
                    c,
                    commonStock.Presentation.Listing.Security.Cusip,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        await using var transaction = await _commonStockRepository.BeginCusipIdentityWrite();
        var alreadyRecorded = await _commonStockRepository
            .GetCusipAliases()
            .Where(a => candidates.Contains(a.Cusip.ToUpper()))
            .Select(a => a.Cusip)
            .ToListAsync();
        var taken = new HashSet<string>(alreadyRecorded, StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(
            await _commonStockRepository
                .GetSecurities()
                .Where(security =>
                    security.Cusip != null && candidates.Contains(security.Cusip.ToUpper())
                )
                .Select(security => security.Cusip)
                .ToListAsync()
        );
        // A CUSIP recorded as a sibling LISTING is a different security's current identity —
        // aliasing it would outrank the listing at resolution time and merge the two classes'
        // positions into one row. One CUSIP identifies one security, in both directions.
        taken.UnionWith(
            await _commonStockRepository
                .GetListedCusips()
                .Where(l => candidates.Contains(l.Cusip.ToUpper()))
                .Select(l => l.Cusip)
                .ToListAsync()
        );

        var recorded = 0;
        foreach (var cusip in candidates.Where(c => !taken.Contains(c)))
        {
            _commonStockRepository.AddCusipAlias(
                new EquityIssuerCusipAlias { EquityIssuerId = commonStock.Id, Cusip = cusip }
            );
            recorded++;
        }

        if (recorded == 0)
        {
            return 0;
        }

        await _commonStockRepository.SaveChanges();
        if (transaction != null)
        {
            await transaction.CommitAsync();
        }

        // Root bus, after the write commits — same reasoning as SetCusip: this flow only
        // saves the financial context, so a bus outbox on another context would capture
        // the publish and never deliver it.
        await _bus.Publish(
            new StockCusipChanged(
                commonStock.Id,
                commonStock.Presentation.Listing.Ticker,
                null,
                commonStock.Presentation.Listing.Security.Cusip
            )
        );

        return recorded;
    }

    /// <summary>
    /// Records the CUSIPs of a stock's OTHER listed securities — the sibling share
    /// classes, units, and fund series named in <see cref="CommonStock.SecondaryTickers"/> —
    /// as <see cref="EquityListingCusipEvidence"/> rows keyed to the exact listed ticker.
    /// <para>
    /// Distinct from <see cref="RecordRetiredCusipAliases"/> on purpose: an alias is a
    /// retired identity of the PRIMARY security, while these are the current identities
    /// of DIFFERENT securities sharing the filer row. The holdings lane resolves a 13F
    /// line filed under one of these CUSIPs to (stock, listed ticker), so the position
    /// imports as its own class instead of being dropped — or worse, merged into the
    /// primary class's row.
    /// </para>
    /// <para>
    /// Callers must have established the pairing from authoritative symbol+CUSIP data.
    /// A CUSIP already claimed anywhere — as a primary, an alias, or another listing —
    /// keeps its first owner (one CUSIP identifies one security, ever). Candidates whose
    /// ticker is not currently one of the stock's secondary tickers are skipped: the
    /// primary's CUSIP identity lives on the stock row and the alias table alone.
    /// </para>
    /// <para>
    /// Recording anything publishes <see cref="StockCusipChanged"/> for the same reason
    /// the alias recorder does: data sets already marked processed hold no rows for
    /// lines filed under the newly-resolvable CUSIP, and the consumer clears that ledger
    /// so the Holdings worker re-imports them. A burst collapses to a no-op once cleared.
    /// </para>
    /// </summary>
    public async Task<int> RecordListedTickerCusips(
        EquityIssuer commonStock,
        IReadOnlyCollection<(string ListedTicker, string Cusip)> candidates,
        IReadOnlyCollection<string> authoritativeHistoricalTickers = null
    )
    {
        ArgumentNullException.ThrowIfNull(commonStock);
        ArgumentNullException.ThrowIfNull(candidates);

        var secondaryTickers = new HashSet<string>(
            commonStock
                .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
                .Where(nativeListing =>
                    nativeListing.MarketCountryCode == "US"
                    && (
                        nativeListing.IsDirectoryListed
                        && nativeListing.Id != commonStock.Presentation.EquityListingId
                    )
                )
                .Select(nativeListing => nativeListing.Ticker)
                .ToList()
                ?? [],
            StringComparer.OrdinalIgnoreCase
        );
        secondaryTickers.UnionWith(authoritativeHistoricalTickers ?? []);
        var cleaned = candidates
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.ListedTicker) && !string.IsNullOrWhiteSpace(c.Cusip)
            )
            .Select(c => (Ticker: c.ListedTicker.Trim(), Cusip: c.Cusip.Trim().ToUpperInvariant()))
            // Store the SecondaryTickers-side spelling, not the caller's: every consumer joins
            // on this string, and one canonical spelling per listing keeps those joins exact.
            .Select(c =>
                secondaryTickers.TryGetValue(c.Ticker, out var canonical)
                    ? (Ticker: canonical, c.Cusip)
                    : c
            )
            .Where(c =>
                secondaryTickers.Contains(c.Ticker)
                && !string.Equals(
                    c.Cusip,
                    commonStock.Presentation.Listing.Security.Cusip,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            // A CUSIP offered under TWO listed tickers is contradictory feed data; keeping an
            // arbitrary pairing would price the class from the other sibling's series. Drop it —
            // the same refusal the sweep applies to ambiguous symbols.
            .GroupBy(c => c.Cusip, StringComparer.OrdinalIgnoreCase)
            .Where(g =>
                g.Select(c => c.Ticker).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            )
            .Select(g => g.First())
            .ToList();
        if (cleaned.Count == 0)
        {
            return 0;
        }

        await using var transaction = await _commonStockRepository.BeginCusipIdentityWrite();
        var candidateCusips = cleaned.Select(c => c.Cusip).ToList();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(
            await _commonStockRepository
                .GetListedCusips()
                .Where(l => candidateCusips.Contains(l.Cusip.ToUpper()))
                .Select(l => l.Cusip)
                .ToListAsync()
        );
        taken.UnionWith(
            await _commonStockRepository
                .GetCusipAliases()
                .Where(a => candidateCusips.Contains(a.Cusip.ToUpper()))
                .Select(a => a.Cusip)
                .ToListAsync()
        );
        taken.UnionWith(
            await _commonStockRepository
                .GetSecurities()
                .Where(security =>
                    security.Cusip != null && candidateCusips.Contains(security.Cusip.ToUpper())
                )
                .Select(security => security.Cusip)
                .ToListAsync()
        );

        var recorded = 0;
        foreach (var candidate in cleaned.Where(c => !taken.Contains(c.Cusip)))
        {
            _commonStockRepository.AddListedCusip(
                new EquityListingCusipEvidence
                {
                    EquityIssuerId = commonStock.Id,
                    ListedTicker = candidate.Ticker,
                    Cusip = candidate.Cusip,
                }
            );
            recorded++;
        }

        if (recorded == 0)
        {
            return 0;
        }

        await _commonStockRepository.SaveChanges();
        if (transaction != null)
        {
            await transaction.CommitAsync();
        }

        // Root bus, after the write commits — same reasoning as SetCusip: this flow only
        // saves the financial context, so a bus outbox on another context would capture
        // the publish and never deliver it.
        await _bus.Publish(
            new StockCusipChanged(
                commonStock.Id,
                commonStock.Presentation.Listing.Ticker,
                null,
                commonStock.Presentation.Listing.Security.Cusip
            )
        );

        return recorded;
    }

    /// <summary>
    /// Sets a stock's CUSIP. When the value actually changes, publishes
    /// <see cref="StockCusipChanged"/> after SaveChanges so the Holdings module can
    /// backfill quarterly 13F data sets that were processed while this stock
    /// was still unresolvable. A no-op change publishes nothing.
    /// <para>
    /// Replacing a non-null CUSIP (an issuer-level CUSIP change) also records the
    /// retired value as a <see cref="EquityIssuerCusipAlias"/>. Filings keep
    /// referencing the old CUSIP — laggard 13F filers for a quarter or two, and
    /// historical data sets forever — so import-time resolution must keep mapping
    /// it to this stock. Without the alias, the backfill triggered by the change
    /// would silently drop old-CUSIP lines wherever a restatement amendment
    /// deletes and re-inserts a quarter.
    /// </para>
    /// <para>
    /// A same-stock alias may be promoted back to current when a newer authoritative
    /// observation reverses a stale designation. An exact listed-ticker claim may be
    /// promoted only with <paramref name="displacedListedTicker"/>: the authoritative
    /// ticker that now owns the displaced CUSIP. This swaps the two designations instead
    /// of collapsing the sibling security into the primary alias set.
    /// </para>
    /// <para>
    /// This is a financial-domain event, so it publishes via the root
    /// <see cref="IBus"/> rather than the scoped <c>IPublishEndpoint</c>. A host
    /// that enables a bus outbox on a different context (e.g. the commercial
    /// customer database) would otherwise capture this publish into that context
    /// and never deliver it, since this flow only saves the financial context.
    /// The consumer is idempotent; a publish lost after the save committed is
    /// not retried here (the next resolve sees the stored value and no-ops), but
    /// the consumer's ledger clear is global, so any later
    /// <see cref="StockCusipChanged"/> from any stock re-imports the missed
    /// data sets and heals the gap.
    /// </para>
    /// </summary>
    public async Task<bool> SetCusip(
        EquityIssuer commonStock,
        string cusip,
        string displacedListedTicker = null
    )
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        var observedCusip = commonStock.Presentation.Listing.Security.Cusip;
        var observedTicker = commonStock.Presentation.Listing.Ticker;
        if (string.Equals(observedCusip, cusip, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedCusip = cusip?.Trim().ToUpperInvariant();
        await using var transaction = await _commonStockRepository.BeginCusipIdentityWrite();
        commonStock =
            await _commonStockRepository.GetForUpdate(commonStock.Id)
            ?? throw new InvalidOperationException(
                $"CommonStock {commonStock.Id} no longer exists."
            );
        if (
            !string.Equals(
                commonStock.Presentation.Listing.Security.Cusip,
                observedCusip,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.Equals(
                commonStock.Presentation.Listing.Ticker,
                observedTicker,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return false;
        if (
            string.Equals(
                commonStock.Presentation.Listing.Security.Cusip,
                normalizedCusip,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return false;

        var securityId = commonStock.Presentation.Listing.EquitySecurityId;
        if (
            await _commonStockRepository
                .GetSecurities()
                .AnyAsync(security =>
                    security.Id != securityId
                    && security.Cusip != null
                    && security.Cusip.ToUpper() == normalizedCusip
                )
        )
            return false;

        var previousCusip = commonStock.Presentation.Listing.Security.Cusip;
        var claims = await GetCusipClaims(normalizedCusip);
        if (claims.PrimaryOwnerIds.Any(ownerId => ownerId != commonStock.Id))
            return false;

        var promotesOwnAlias =
            claims.Aliases.Count > 0
            && claims.Aliases.All(alias => alias.EquityIssuerId == commonStock.Id);
        var promotesExactListing =
            claims.Listings.Count > 0
            && claims.Listings.All(listing =>
                listing.EquityIssuerId == commonStock.Id
                && string.Equals(
                    listing.ListedTicker,
                    commonStock.Presentation.Listing.Ticker,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        if (
            (claims.Aliases.Count > 0 && !promotesOwnAlias)
            || (claims.Listings.Count > 0 && !promotesExactListing)
        )
            return false;

        if (promotesExactListing)
        {
            if (
                !await TryStageExactListingPromotion(
                    commonStock,
                    previousCusip,
                    displacedListedTicker,
                    claims.Aliases,
                    claims.Listings
                )
            )
                return false;
        }
        else
        {
            if (promotesOwnAlias)
            {
                foreach (var alias in claims.Aliases)
                    _commonStockRepository.DeleteCusipAlias(alias);
            }
            await StageRetiredCusip(commonStock, previousCusip);
        }

        commonStock.Presentation.Listing.Security.Cusip = normalizedCusip;

        await _commonStockRepository.SaveChanges();
        if (transaction != null)
        {
            await transaction.CommitAsync();
        }

        // Publish via the root bus (bypasses any bus outbox) after the write commits.
        await _bus.Publish(
            new StockCusipChanged(
                commonStock.Id,
                commonStock.Presentation.Listing.Ticker,
                previousCusip,
                normalizedCusip
            )
        );
        return true;
    }

    private async Task<(
        List<Guid> PrimaryOwnerIds,
        List<EquityIssuerCusipAlias> Aliases,
        List<EquityListingCusipEvidence> Listings
    )> GetCusipClaims(string normalizedCusip)
    {
        var primaryOwnerIds = await _commonStockRepository
            .GetSecurities()
            .Where(security =>
                security.Cusip != null && security.Cusip.ToUpper() == normalizedCusip
            )
            .Select(security => security.EquityIssuerId)
            .ToListAsync();
        var aliases = await _commonStockRepository
            .GetCusipAliases()
            .Where(candidate => candidate.Cusip.ToUpper() == normalizedCusip)
            .ToListAsync();
        var listings = await _commonStockRepository
            .GetListedCusips()
            .Where(candidate => candidate.Cusip.ToUpper() == normalizedCusip)
            .ToListAsync();
        return (primaryOwnerIds, aliases, listings);
    }

    private async Task<bool> TryStageExactListingPromotion(
        EquityIssuer commonStock,
        string previousCusip,
        string displacedListedTicker,
        IReadOnlyCollection<EquityIssuerCusipAlias> promotedAliases,
        IReadOnlyCollection<EquityListingCusipEvidence> promotedListings
    )
    {
        var displacedTicker = displacedListedTicker?.Trim().ToUpperInvariant();
        if (
            previousCusip == null
            || displacedTicker == null
            || !commonStock
                .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
                .Where(nativeListing =>
                    nativeListing.MarketCountryCode == "US"
                    && (
                        nativeListing.IsDirectoryListed
                        && nativeListing.Id != commonStock.Presentation.EquityListingId
                    )
                )
                .Select(nativeListing => nativeListing.Ticker)
                .ToList()
                .Contains(displacedTicker, StringComparer.OrdinalIgnoreCase)
        )
            return false;

        var claims = await GetDisplacedCusipClaims(commonStock, previousCusip);
        if (!CanAssignDisplacedListing(commonStock, displacedTicker, claims))
            return false;

        foreach (var alias in promotedAliases)
            _commonStockRepository.DeleteCusipAlias(alias);
        foreach (var listing in promotedListings)
            _commonStockRepository.DeleteListedCusip(listing);
        foreach (var alias in claims.Aliases)
            _commonStockRepository.DeleteCusipAlias(alias);
        if (claims.Listings.Count == 0)
        {
            _commonStockRepository.AddListedCusip(
                new EquityListingCusipEvidence
                {
                    EquityIssuerId = commonStock.Id,
                    ListedTicker = displacedTicker,
                    Cusip = previousCusip.ToUpperInvariant(),
                }
            );
        }
        return true;
    }

    private async Task<(
        bool PrimaryClaimedElsewhere,
        List<EquityIssuerCusipAlias> Aliases,
        List<EquityListingCusipEvidence> Listings
    )> GetDisplacedCusipClaims(EquityIssuer commonStock, string previousCusip)
    {
        var normalized = previousCusip.ToUpperInvariant();
        var primaryClaimedElsewhere = await _commonStockRepository
            .GetAll()
            .AnyAsync(stock =>
                stock.Id != commonStock.Id
                && stock.Securities.Any(security =>
                    security.Cusip != null && security.Cusip.ToUpper() == normalized
                )
            );
        var aliases = await _commonStockRepository
            .GetCusipAliases()
            .Where(alias => alias.Cusip.ToUpper() == normalized)
            .ToListAsync();
        var listings = await _commonStockRepository
            .GetListedCusips()
            .Where(listing => listing.Cusip.ToUpper() == normalized)
            .ToListAsync();
        return (primaryClaimedElsewhere, aliases, listings);
    }

    private static bool CanAssignDisplacedListing(
        EquityIssuer commonStock,
        string displacedTicker,
        (
            bool PrimaryClaimedElsewhere,
            List<EquityIssuerCusipAlias> Aliases,
            List<EquityListingCusipEvidence> Listings
        ) claims
    ) =>
        !claims.PrimaryClaimedElsewhere
        && claims.Aliases.All(alias => alias.EquityIssuerId == commonStock.Id)
        && claims.Listings.All(listing =>
            listing.EquityIssuerId == commonStock.Id
            && string.Equals(
                listing.ListedTicker,
                displacedTicker,
                StringComparison.OrdinalIgnoreCase
            )
        );

    private async Task StageRetiredCusip(EquityIssuer commonStock, string previousCusip)
    {
        if (previousCusip == null)
            return;

        var normalized = previousCusip.ToUpperInvariant();
        var alreadyClaimed =
            await _commonStockRepository
                .GetCusipAliases()
                .AnyAsync(alias => alias.Cusip.ToUpper() == normalized)
            || await _commonStockRepository
                .GetListedCusips()
                .AnyAsync(listing => listing.Cusip.ToUpper() == normalized);
        if (!alreadyClaimed)
        {
            _commonStockRepository.AddCusipAlias(
                new EquityIssuerCusipAlias { EquityIssuerId = commonStock.Id, Cusip = normalized }
            );
        }
    }

    /// <summary>
    /// Attaches an additional CIK to a stock — a predecessor registrant after a holdco
    /// reorganisation (Exxon's 2026 reorg moved XOM to a new CIK and left the entire
    /// filing and fact history on the old one, GH-7041) or a co-registrant subsidiary.
    /// Operator-driven: no SEC feed states successorship in structured form, so the
    /// association is a human decision; everything downstream is automatic — filing
    /// discovery and the document scraper enumerate every attached CIK each sweep, and
    /// the published <see cref="StockSecondaryCikAttached"/> resets the financial-facts
    /// checkpoint so the predecessor's full XBRL history imports on the next cycle.
    /// Returns null on success, otherwise a reason the attachment was refused.
    /// </summary>
    public async Task<string> AttachSecondaryCik(EquityIssuer commonStock, string cik)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        var normalized = NormalizeCik(cik);
        if (normalized == null)
        {
            return $"'{cik}' is not a valid CIK — expected up to ten digits.";
        }
        if (normalized == NormalizeCik(commonStock.Cik))
        {
            return $"CIK {normalized} is already this stock's primary CIK.";
        }
        if (commonStock.SecondaryCiks.Contains(normalized))
        {
            return $"CIK {normalized} is already attached to this stock.";
        }

        // One CIK belongs to one stock, ever — attaching a CIK another row owns
        // (as primary or secondary) would merge two companies' filings.
        var owner = await _commonStockRepository
            .GetAll()
            .Where(cs => cs.Cik == normalized || cs.SecondaryCiks.Contains(normalized))
            .Select(cs => new
            {
                DisplayName = cs.Presentation == null
                    ? cs.Name ?? cs.Cik
                    : cs.Presentation.Listing.Ticker,
            })
            .FirstOrDefaultAsync();
        if (owner != null)
        {
            return $"CIK {normalized} already belongs to {owner.DisplayName}.";
        }

        commonStock.SecondaryCiks = [.. commonStock.SecondaryCiks, normalized];
        await _commonStockRepository.SaveChanges();

        // Publish via the root bus (bypasses any bus outbox) after the write commits.
        await _bus.Publish(
            new StockSecondaryCikAttached(
                commonStock.Id,
                commonStock.Presentation?.Listing?.Ticker,
                normalized
            )
        );
        return null;
    }

    /// <summary>
    /// Removes a previously attached secondary CIK. Already-imported documents and
    /// facts stay (they were the operator's deliberate backfill); the scrapers simply
    /// stop sweeping the detached CIK. Returns null on success, otherwise a reason.
    /// </summary>
    public async Task<string> DetachSecondaryCik(EquityIssuer commonStock, string cik)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        // Normalize BOTH sides: AttachSecondaryCik always stores normalized values,
        // but the company sync's subsidiary attach writes SEC's value verbatim — a
        // zero-padded stored CIK must still be removable.
        var normalized = NormalizeCik(cik);
        if (
            normalized == null
            || !commonStock.SecondaryCiks.Any(c => NormalizeCik(c) == normalized)
        )
        {
            return $"CIK {cik} is not attached to this stock.";
        }

        commonStock.SecondaryCiks = commonStock
            .SecondaryCiks.Where(c => NormalizeCik(c) != normalized)
            .ToList();
        await _commonStockRepository.SaveChanges();
        return null;
    }

    // CIKs are stored with leading zeros trimmed (matching SEC filer CIKs elsewhere in
    // the model); a CIK is one to ten digits.
    internal static string NormalizeCik(string cik)
    {
        var trimmed = cik?.Trim().TrimStart('0');
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 10 || !trimmed.All(char.IsAsciiDigit))
        {
            return null;
        }
        return trimmed;
    }

    /// <summary>
    /// Stages a <see cref="EquityIssuerTickerAlias"/> for a primary ticker the stock is
    /// abandoning, so URLs published under the old symbol can 301 to the current one.
    /// Stages only — no SaveChanges: the caller is the SEC sync mid-rename, and the alias
    /// must commit (or roll back) atomically with the rename itself.
    /// <para>
    /// Semantics differ from the CUSIP alias on purpose. A CUSIP identifies one security
    /// forever, so the first owner keeps a retired CUSIP; tickers are recycled across
    /// unrelated issuers, so redirects are last-writer-wins:
    /// an alias equal to any LIVE primary or secondary ticker is never recorded (the live
    /// symbol would shadow it anyway — recording it would only seed a stale row for the
    /// day the live holder renames); and recording a symbol another stock retired earlier
    /// deletes that stale alias first — the most recent holder owns the redirect.
    /// </para>
    /// Returns the staged entity, or null when nothing was staged, so the caller can
    /// detach it if the surrounding update is rolled back.
    /// </summary>
    public async Task<EquityIssuerTickerAlias> RecordTickerAlias(
        EquityIssuer commonStock,
        string retiredTicker
    )
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        if (
            string.IsNullOrWhiteSpace(retiredTicker)
            || string.Equals(
                retiredTicker.Trim(),
                commonStock.Presentation?.Listing?.Ticker,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        var normalized = retiredTicker.Trim().ToUpperInvariant();

        // A retained live listing is not a retired URL, regardless of the presentation choice.
        if (
            commonStock.Securities.Any(security =>
                security.Listings.Any(listing =>
                    listing.MarketCountryCode == "US"
                    && listing.Active
                    && (listing.IsDirectoryListed || listing.IsReferenceListed)
                    && string.Equals(listing.Ticker, normalized, StringComparison.OrdinalIgnoreCase)
                )
            )
        )
            return null;

        // Never shadow a live symbol: if any OTHER stock currently lists it (primary or
        // secondary), the live resolution wins on every lookup and the alias would only
        // linger as a wrong redirect after that holder eventually renames. The caller is the
        // sync MID-RENAME — this stock's own row still holds the retired symbol in the
        // database (the new ticker is staged in memory, unflushed, and EF never flushes
        // before a query) — so the check must exclude the stock itself or it matches its own
        // stale row and no alias is ever recorded on the one path that matters.
        var liveHolder = await _commonStockRepository
            .GetAll()
            .AnyAsync(issuer =>
                issuer.Id != commonStock.Id
                && issuer.Securities.Any(security =>
                    security.Listings.Any(listing =>
                        listing.MarketCountryCode == "US"
                        && listing.Active
                        && (listing.IsDirectoryListed || listing.IsReferenceListed)
                        && listing.Ticker == normalized
                    )
                )
            );
        if (liveHolder)
        {
            return null;
        }

        // Re-adoption cleanup — the deletion half of last-writer-wins the redirect design
        // depends on: the symbol this stock is renaming TO may sit in the alias map from an
        // earlier retirement (its own A→B→A round trip, or another issuer's). Once it is live
        // again the alias is at best shadowed and at worst a wrong redirect, so it goes.
        var adopted = commonStock.Presentation?.Listing?.Ticker?.ToUpperInvariant();
        if (adopted != null)
        {
            var staleAdopted = await _commonStockRepository
                .GetTickerAliases()
                .FirstOrDefaultAsync(a => a.Ticker == adopted);
            if (staleAdopted != null)
            {
                _commonStockRepository.DeleteTickerAlias(staleAdopted);
            }
        }

        // Last-writer-wins: a symbol another stock retired earlier now belongs to this
        // stock's history — delete the stale alias so the unique index accepts the new
        // row (also covers this stock re-retiring a symbol it held twice).
        var existing = await _commonStockRepository
            .GetTickerAliases()
            .FirstOrDefaultAsync(a => a.Ticker == normalized);
        if (existing != null)
        {
            if (existing.EquityIssuerId == commonStock.Id)
            {
                return null;
            }
            _commonStockRepository.DeleteTickerAlias(existing);
        }

        return _commonStockRepository.AddTickerAlias(
            new EquityIssuerTickerAlias { EquityIssuerId = commonStock.Id, Ticker = normalized }
        );
    }

    /// <summary>
    /// Sets the company's fiscal year-end (month 1-12, optional day 1-31),
    /// sourced from SEC EDGAR's submissions <c>fiscalYearEnd</c> field. A
    /// no-op change persists nothing. Saves directly via the repository — like
    /// <see cref="SetCusip"/>, this mutates a single non-key field and must not
    /// re-run the full ticker/CIK uniqueness validation.
    /// </summary>
    public async Task SetFiscalYearEnd(EquityIssuer commonStock, int month, int? day)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        if (month is < 1 or > 12)
        {
            throw new DomainValidationException(
                $"Fiscal year-end month must be between 1 and 12, got {month}"
            );
        }

        if (day is < 1 or > 31)
        {
            throw new DomainValidationException(
                $"Fiscal year-end day must be between 1 and 31, got {day}"
            );
        }

        if (day is not null && day > DateTime.DaysInMonth(2000, month))
        {
            throw new DomainValidationException($"Day {day} is invalid for month {month}");
        }

        if (commonStock.FiscalYearEndMonth == month && commonStock.FiscalYearEndDay == day)
        {
            return;
        }

        commonStock.FiscalYearEndMonth = month;
        commonStock.FiscalYearEndDay = day;
        await _commonStockRepository.SaveChanges();
    }

    /// <summary>
    /// Sets the company's SEC classification — the submissions <c>sic</c> code and
    /// <c>entityType</c> — used to tell operating companies apart from pooled
    /// investment vehicles. Blank values are normalised to null so a missing SIC
    /// stays eligible for a later refill rather than masquerading as classified. A
    /// no-op change persists nothing. Saves directly via the repository — like
    /// <see cref="SetFiscalYearEnd"/>, this mutates non-key fields and must not
    /// re-run the full ticker/CIK uniqueness validation.
    /// </summary>
    public async Task SetSecClassification(EquityIssuer commonStock, string sic, string entityType)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        var normalizedSic = string.IsNullOrWhiteSpace(sic) ? null : sic.Trim();
        var normalizedEntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim();

        if (commonStock.Sic == normalizedSic && commonStock.EntityType == normalizedEntityType)
        {
            return;
        }

        commonStock.Sic = normalizedSic;
        commonStock.EntityType = normalizedEntityType;
        await _commonStockRepository.SaveChanges();
    }

    public async Task<EquityIssuer> Create(EquityIssuer commonStock)
    {
        ValidateValues(commonStock);
        await using var transaction = await _commonStockRepository.BeginDirectoryIdentityWrite();
        await ValidateCommonStock(commonStock, true);
        _commonStockRepository.Add(commonStock);
        await _commonStockRepository.SaveChanges();
        if (transaction != null)
            await transaction.CommitAsync();
        return commonStock;
    }

    public async Task<EquityIssuer> Update(EquityIssuer commonStock)
    {
        ValidateValues(commonStock);
        await using var transaction = await _commonStockRepository.BeginDirectoryIdentityWrite();
        await _commonStockRepository.LockIssuerForDirectoryWrite(commonStock.Id);
        await ValidateCommonStock(commonStock, false);
        await _commonStockRepository.SaveChanges();
        if (transaction != null)
            await transaction.CommitAsync();
        return commonStock;
    }

    private async Task ValidateCommonStock(EquityIssuer commonStock, bool isInsert)
    {
        ValidateValues(commonStock);
        var primary = commonStock.Presentation?.Listing;
        if (primary?.MarketCountryCode == "US")
        {
            var existing = await _commonStockRepository.GetPrimaryUsByTicker(primary.Ticker);
            if (existing != null && (isInsert || existing.Id != commonStock.Id))
                throw new DomainValidationException(
                    $"Issuer with ticker {primary.Ticker} already exists"
                );
        }
        if (commonStock.Cik != null)
        {
            var existing = await _commonStockRepository.GetByCik(commonStock.Cik);
            if (existing != null && (isInsert || existing.Id != commonStock.Id))
                throw new DomainValidationException(
                    $"Issuer with cik {commonStock.Cik} already exists"
                );
        }
    }

    private static void ValidateValues(EquityIssuer commonStock)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        RequireNonBlank(commonStock.Name, "Name");
        if (commonStock.Cik != null)
            RequireNonBlank(commonStock.Cik, "Cik");
        foreach (var security in commonStock.Securities)
        {
            if (security.MarketCapitalization < 0)
                throw new DomainValidationException("MarketCapitalization cannot be negative");
            if (security.SharesOutstanding < 0)
                throw new DomainValidationException("SharesOutStanding cannot be negative");
            foreach (var listing in security.Listings)
                RequireNonBlank(listing.Ticker, "Ticker");
        }
    }

    private static void RequireNonBlank(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{name} is required");
        }
    }
}
