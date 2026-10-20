using Equibles.CommonStocks.Data.Helpers;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services.Signals;

/// <summary>
/// Insiders' shares in each issuer as of a quarter end, read the way GetInsiderOwnership reads a
/// current position (<see cref="InsiderOwnershipPositionCalculator.Build"/>): each insider's newest
/// Form 3, 4 or 5 on or before the date, its closing balance per vehicle, restated onto the
/// quarter's basis. An insider whose newest filing is older than <see cref="MaximumAgeMonths"/>
/// has left the count, and an insider that files 13F books is an institution, not float.
/// </summary>
internal static class InsiderSharesAsOfLoader
{
    /// <summary>An insider silent this long before the date no longer counts.</summary>
    public const int MaximumAgeMonths = 24;

    private const int ChunkSize = 2_000;

    public static async Task<Dictionary<Guid, long>> Load(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        IReadOnlyCollection<Guid> issuerIds,
        IReadOnlySet<string> institutionCiks,
        IReadOnlyDictionary<Guid, List<StockSplit>> primarySplits,
        CancellationToken cancellationToken
    )
    {
        if (issuerIds.Count == 0)
            return [];
        var earliest = reportDate.AddMonths(-MaximumAgeMonths);
        var issuers = issuerIds.ToArray();
        // The newest actual-share row per issuer and insider, in the order a current position
        // reads it; its filing's rows hold every closing balance.
        var newest = await dbContext
            .Database.SqlQuery<NewestInsiderRow>(
                $"""
                SELECT DISTINCT ON ("EquityIssuerId", "InsiderOwnerId")
                       "EquityIssuerId", "InsiderOwnerId", "AccessionNumber", "Id"
                FROM "InsiderTransaction"
                WHERE "SecurityKind" = {(int)InsiderSecurityKind.NonDerivative}
                  AND "TransactionDate" <= {reportDate}
                  AND "TransactionDate" > {earliest}
                  AND "EquityIssuerId" = ANY({issuers})
                ORDER BY "EquityIssuerId", "InsiderOwnerId", "TransactionDate" DESC,
                         "FilingDate" DESC, "AccessionNumber" DESC, "TransactionOrder" DESC, "Id" DESC
                """
            )
            .ToListAsync(cancellationToken);
        if (newest.Count == 0)
            return [];

        var ownerIds = newest.Select(row => row.InsiderOwnerId).Distinct().ToList();
        var institutionOwners = new HashSet<Guid>();
        foreach (var chunk in ownerIds.Chunk(ChunkSize))
        {
            var owners = await dbContext
                .Set<InsiderOwner>()
                .AsNoTracking()
                .Where(owner => chunk.Contains(owner.Id))
                .Select(owner => new { owner.Id, owner.OwnerCik })
                .ToListAsync(cancellationToken);
            foreach (var owner in owners)
                if (
                    CikNormalizer.Canonicalize(owner.OwnerCik) is { } cik
                    && institutionCiks.Contains(cik)
                )
                    institutionOwners.Add(owner.Id);
        }

        var wanted = newest.Where(row => !institutionOwners.Contains(row.InsiderOwnerId)).ToList();
        var rows = new List<InsiderTransaction>();
        // A filing's rows share its accession; a row without one stands alone.
        foreach (var chunk in wanted.Where(row => row.AccessionNumber != null).Chunk(ChunkSize))
        {
            var accessions = chunk.Select(row => row.AccessionNumber).Distinct().ToList();
            rows.AddRange(
                await LoadRows(
                    dbContext,
                    reportDate,
                    row => accessions.Contains(row.AccessionNumber),
                    cancellationToken
                )
            );
        }
        foreach (var chunk in wanted.Where(row => row.AccessionNumber == null).Chunk(ChunkSize))
        {
            var ids = chunk.Select(row => row.Id).ToList();
            rows.AddRange(
                await LoadRows(
                    dbContext,
                    reportDate,
                    row => ids.Contains(row.Id),
                    cancellationToken
                )
            );
        }

        var keys = wanted
            .Select(row => (row.EquityIssuerId, row.InsiderOwnerId, row.AccessionNumber, row.Id))
            .ToList();
        var byOwner = rows.ToLookup(row => (row.EquityIssuerId, row.InsiderOwnerId));
        var totals = new Dictionary<Guid, long>();
        foreach (var (issuerId, ownerId, accession, id) in keys)
        {
            var filing = byOwner[(issuerId, ownerId)]
                .Where(row => accession != null ? row.AccessionNumber == accession : row.Id == id)
                .ToList();
            if (filing.Count == 0)
                continue;
            var position = InsiderOwnershipPositionCalculator.Build(
                filing,
                primarySplits.GetValueOrDefault(issuerId) ?? []
            );
            if (position.TotalShares > 0)
                totals[issuerId] = totals.GetValueOrDefault(issuerId) + position.TotalShares;
        }
        return totals;
    }

    // Only the columns the closing-balance rules read, and only rows dated by the quarter end, so a
    // filing that spans it closes at its last trade inside. A joint filing's other owners and
    // other issuers stay out by the caller's key.
    private static Task<List<InsiderTransaction>> LoadRows(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        System.Linq.Expressions.Expression<Func<InsiderTransaction, bool>> filter,
        CancellationToken cancellationToken
    ) =>
        dbContext
            .Set<InsiderTransaction>()
            .AsNoTracking()
            .Where(row =>
                row.SecurityKind == InsiderSecurityKind.NonDerivative
                && row.TransactionDate <= reportDate
            )
            .Where(filter)
            .Select(row => new InsiderTransaction
            {
                Id = row.Id,
                InsiderOwnerId = row.InsiderOwnerId,
                EquityIssuerId = row.EquityIssuerId,
                FilingDate = row.FilingDate,
                TransactionDate = row.TransactionDate,
                TransactionCode = row.TransactionCode,
                Shares = row.Shares,
                AcquiredDisposed = row.AcquiredDisposed,
                SharesOwnedAfter = row.SharesOwnedAfter,
                OwnershipNature = row.OwnershipNature,
                NatureOfOwnership = row.NatureOfOwnership,
                OwnershipNatureFootnoteIds = row.OwnershipNatureFootnoteIds,
                SecurityTitle = row.SecurityTitle,
                AccessionNumber = row.AccessionNumber,
                TransactionOrder = row.TransactionOrder,
                SecurityKind = row.SecurityKind,
            })
            .ToListAsync(cancellationToken);

    private sealed class NewestInsiderRow
    {
        public Guid EquityIssuerId { get; init; }
        public Guid InsiderOwnerId { get; init; }
        public string AccessionNumber { get; init; }
        public Guid Id { get; init; }
    }
}
