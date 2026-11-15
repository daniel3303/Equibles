using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Moves a fund's listed-CUSIP claims, and the positions stored through them, to the issuer that
/// lists the fund today.
/// </summary>
/// <remarks>
/// <para>
/// A listed-CUSIP claim is read once from the fails archive and recorded on the issuer that
/// carried the symbol that day; one CUSIP keeps its first owner. When the directory later lists
/// the symbol under the fund's own trust and retires the earlier listing, nothing moved the claim,
/// so every position filed under that CUSIP kept landing on the earlier issuer: the fund's page
/// showed no holders and its sponsor's page showed the fund's.
/// </para>
/// <para>
/// A claim moves only on stored facts, never a name or a ticker shape: the earlier issuer's
/// listing of the symbol is retired, was never reference listed and carries no delisting date (a
/// listing that never was the security's market, not a fund that closed and gave up its symbol);
/// exactly one other issuer lists the exact symbol live, as a reference listing that is not its
/// presentation and holds no CUSIP of its own; exactly one SEC fund series carries the symbol as
/// a class ticker and belongs to that issuer; and the claim's CUSIP carries an issuer number
/// (<see cref="Equibles.CommonStocks.Data.Helpers.CusipIdentity"/>) that issuer already holds in
/// a security, an alias or another claim.
/// </para>
/// <para>
/// The issuer number is what keeps a recycled symbol out. The earlier issuer can hold two claims
/// for one symbol: the fund's CUSIP and the CUSIP of the security that used the symbol before it,
/// its own stock included. Only the fund's shares an issuer number with the trust's other funds,
/// so the earlier security's positions stay where they are, as do a fund's whose trust holds no
/// CUSIP yet to compare with.
/// </para>
/// <para>
/// The claims and their positions move in one transaction under the CUSIP identity lock and both
/// issuer locks, so an import between the two can never split one filer across both issuers.
/// </para>
/// </remarks>
[Service]
public class ListedCusipTransferService
{
    /// <summary>What one pass moved, and what it had to leave behind.</summary>
    public sealed record Result(int Funds, int Claims, int Positions, int Occupied, int Failed = 0);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListedCusipTransferService> _logger;

    public ListedCusipTransferService(
        IServiceScopeFactory scopeFactory,
        ILogger<ListedCusipTransferService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// The claims the rule admits. The two filters narrow it to one issuer's claims for one symbol,
    /// which is how a transfer re-reads its own fund once it holds the locks.
    /// </summary>
    /// <remarks>
    /// Staged so the retired-listing test, which only reads listings by symbol, cuts the claims
    /// down before the fund-series lookups run; the unstaged form evaluated those for every claim.
    /// </remarks>
    internal static IQueryable<ListedCusipTransferCandidate> BuildCandidateQuery(
        EquiblesFinancialDbContext dbContext,
        Guid? fromIssuerId,
        string listedTicker
    ) =>
        dbContext.Database.SqlQuery<ListedCusipTransferCandidate>(
            $"""
            WITH own AS MATERIALIZED (
                SELECT e."Id", e."EquityIssuerId", e."ListedTicker", e."Cusip"
                FROM "EquityListingCusipEvidence" AS e
                WHERE ({fromIssuerId}::uuid IS NULL OR e."EquityIssuerId" = {fromIssuerId}::uuid)
                  AND ({listedTicker}::text IS NULL OR e."ListedTicker" = {listedTicker}::text)
                  AND EXISTS (
                      SELECT 1 FROM "EquityListing" AS l
                      JOIN "EquitySecurity" AS s ON s."Id" = l."EquitySecurityId"
                      WHERE l."MarketCountryCode" = 'US' AND l."Ticker" = e."ListedTicker"
                        AND s."EquityIssuerId" = e."EquityIssuerId")
                  AND NOT EXISTS (
                      SELECT 1 FROM "EquityListing" AS l
                      JOIN "EquitySecurity" AS s ON s."Id" = l."EquitySecurityId"
                      WHERE l."MarketCountryCode" = 'US' AND l."Ticker" = e."ListedTicker"
                        AND s."EquityIssuerId" = e."EquityIssuerId"
                        AND (l."Active" OR l."WasReferenceListed" OR l."IsReferenceListed"
                             OR l."DelistedOn" IS NOT NULL))
            ), live AS MATERIALIZED (
                SELECT own."Id", own."EquityIssuerId", own."ListedTicker", own."Cusip",
                       lister."ToIssuerId"
                FROM own
                JOIN LATERAL (
                    SELECT min(s."EquityIssuerId"::text)::uuid AS "ToIssuerId",
                           count(DISTINCT s."EquityIssuerId") AS "Issuers",
                           bool_and(l."IsReferenceListed") AS "Reference",
                           bool_or(p."EquityListingId" = l."Id") AS "Presentation",
                           bool_or(s."Cusip" IS NOT NULL) AS "OwnCusip"
                    FROM "EquityListing" AS l
                    JOIN "EquitySecurity" AS s ON s."Id" = l."EquitySecurityId"
                    LEFT JOIN "EquityIssuerPresentation" AS p
                      ON p."EquityIssuerId" = s."EquityIssuerId"
                    WHERE l."MarketCountryCode" = 'US' AND l."Ticker" = own."ListedTicker"
                      AND l."Active" AND s."EquityIssuerId" <> own."EquityIssuerId"
                ) AS lister ON lister."Issuers" = 1 AND lister."Reference"
                    AND NOT coalesce(lister."Presentation", false) AND NOT lister."OwnCusip"
            )
            SELECT live."Id" AS "EvidenceId", live."EquityIssuerId" AS "FromIssuerId",
                   live."ToIssuerId", live."ListedTicker", live."Cusip"
            FROM live
            WHERE length(live."Cusip") >= 6
              AND (EXISTS (
                      SELECT 1 FROM "EquityListingCusipEvidence" AS held
                      WHERE held."EquityIssuerId" = live."ToIssuerId"
                        AND upper(left(held."Cusip", 6)) = upper(left(live."Cusip", 6)))
                   OR EXISTS (
                      SELECT 1 FROM "EquitySecurity" AS held
                      WHERE held."EquityIssuerId" = live."ToIssuerId"
                        AND upper(left(held."Cusip", 6)) = upper(left(live."Cusip", 6)))
                   OR EXISTS (
                      SELECT 1 FROM "EquityIssuerCusipAlias" AS held
                      WHERE held."EquityIssuerId" = live."ToIssuerId"
                        AND upper(left(held."Cusip", 6)) = upper(left(live."Cusip", 6))))
              AND (SELECT count(*) FROM "FundSeries" AS f
                   WHERE f."ClassTickers" @> ARRAY[live."ListedTicker"::text]) = 1
              AND EXISTS (
                      SELECT 1 FROM "FundSeries" AS f
                      WHERE f."ClassTickers" @> ARRAY[live."ListedTicker"::text]
                        AND f."EquityIssuerId" = live."ToIssuerId")
            ORDER BY live."EquityIssuerId", live."ListedTicker", live."Cusip"
            """
        );

    /// <summary>Moves every admitted fund and reports what moved.</summary>
    public async Task<Result> Transfer(CancellationToken cancellationToken)
    {
        List<ListedCusipTransferCandidate> candidates;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
            candidates = await BuildCandidateQuery(dbContext, null, null)
                .ToListAsync(cancellationToken);
        }

        var funds = 0;
        var claims = 0;
        var positions = 0;
        var occupied = 0;
        var failed = 0;
        foreach (
            var fund in candidates.GroupBy(candidate =>
                (candidate.FromIssuerId, candidate.ListedTicker)
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One scope per fund: a failed transfer leaves no tracked state for the next.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
            var stockRepo = scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();
            (int Positions, int Occupied)? moved;
            try
            {
                moved = await TransferFund(dbContext, stockRepo, [.. fund], cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One fund's lock timeout or deadlock with an import must not starve the funds
                // behind it; its transaction rolled back whole, so the next cycle retries it.
                failed++;
                _logger.LogWarning(
                    exception,
                    "Listed-CUSIP transfer: {ListedTicker} of issuer {FromIssuerId} was not moved",
                    fund.Key.ListedTicker,
                    fund.Key.FromIssuerId
                );
                continue;
            }
            if (moved == null)
                continue;
            funds++;
            claims += fund.Count();
            positions += moved.Value.Positions;
            occupied += moved.Value.Occupied;
        }

        if (funds > 0)
            _logger.LogInformation(
                "Listed-CUSIP transfer: {Funds} fund(s) with {Claims} claim(s) moved to the issuer listing their symbol, {Positions} stored position(s) with them",
                funds,
                claims,
                positions
            );
        if (occupied > 0)
            _logger.LogWarning(
                "Listed-CUSIP transfer: {Occupied} stored position(s) stayed with their earlier issuer because the listing issuer already holds that position",
                occupied
            );
        return new Result(funds, claims, positions, occupied, failed);
    }

    private static async Task<(int Positions, int Occupied)?> TransferFund(
        EquiblesFinancialDbContext dbContext,
        EquityIssuerRepository stockRepo,
        List<ListedCusipTransferCandidate> claims,
        CancellationToken cancellationToken
    )
    {
        var fromIssuerId = claims[0].FromIssuerId;
        var toIssuerId = claims[0].ToIssuerId;
        var listedTicker = claims[0].ListedTicker;

        // The CUSIP identity lock comes before the issuer locks, the order every identity writer
        // takes them in. The two issuers are locked in id order, as an import locks its batch, so
        // the pass and an import wait for each other and never deadlock on these rows.
        await using var transaction = await stockRepo.BeginCusipIdentityWrite(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT "Id" FROM "EquityIssuer" WHERE "Id" IN ({fromIssuerId}, {toIssuerId})
            ORDER BY "Id" FOR NO KEY UPDATE
            """,
            cancellationToken
        );
        // The directory or the claims may have changed since the pass listed this fund.
        var current = await BuildCandidateQuery(dbContext, fromIssuerId, listedTicker)
            .ToListAsync(cancellationToken);
        if (
            current.Count != claims.Count
            || current.Any(claim => claim.ToIssuerId != toIssuerId)
            || !current
                .Select(claim => claim.EvidenceId)
                .ToHashSet()
                .SetEquals(claims.Select(claim => claim.EvidenceId))
        )
            return null;

        var evidenceIds = claims.Select(claim => claim.EvidenceId).ToArray();
        var cusips = claims.Select(claim => claim.Cusip).ToArray();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "EquityListingCusipEvidence" SET "EquityIssuerId" = {toIssuerId}
            WHERE "Id" = ANY ({evidenceIds})
            """,
            cancellationToken
        );
        // Every filing type: a Schedule 13D/G row filed under the CUSIP names the same security.
        // The label is matched with '=' so the unique index serves it. A position the listing
        // issuer already holds under the same key stays where it is; moving it would merge two
        // stored rows. Manager legs hang on the position's id and travel with it.
        var dates = await dbContext
            .Database.SqlQuery<DateOnly>(
                $"""
                UPDATE "InstitutionalHolding" AS h SET "EquityIssuerId" = {toIssuerId}
                WHERE h."EquityIssuerId" = {fromIssuerId}
                  AND h."ListedTicker" = {listedTicker}
                  AND h."Cusip" = ANY ({cusips})
                  AND NOT EXISTS (
                      SELECT 1 FROM "InstitutionalHolding" AS o
                      WHERE o."EquityIssuerId" = {toIssuerId}
                        AND o."InstitutionalHolderId" = h."InstitutionalHolderId"
                        AND o."ReportDate" = h."ReportDate"
                        AND o."ShareType" = h."ShareType"
                        AND o."OptionType" IS NOT DISTINCT FROM h."OptionType"
                        AND o."FilingType" = h."FilingType"
                        AND o."ListedTicker" = h."ListedTicker")
                RETURNING h."ReportDate" AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        var occupied = await dbContext
            .Database.SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value" FROM "InstitutionalHolding" AS h
                WHERE h."EquityIssuerId" = {fromIssuerId}
                  AND h."ListedTicker" = {listedTicker}
                  AND h."Cusip" = ANY ({cusips})
                """
            )
            .SingleAsync(cancellationToken);
        // The move and its rebuild intent commit together: once the positions sit under the
        // listing issuer, a retry cannot tell which quarters they left.
        await HoldingsRollupRefresher.MarkAumSnapshotsDirty(
            dbContext,
            dates.Distinct().ToList(),
            cancellationToken
        );
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
        return (dates.Count, occupied);
    }
}

/// <summary>
/// One claim the transfer rule admits, with the issuer that lists its symbol today. Public and
/// unsealed because the proxy convention validates every type a SqlQuery materializes.
/// </summary>
public class ListedCusipTransferCandidate
{
    public Guid EvidenceId { get; set; }
    public Guid FromIssuerId { get; set; }
    public Guid ToIssuerId { get; set; }
    public string ListedTicker { get; set; }
    public string Cusip { get; set; }
}
