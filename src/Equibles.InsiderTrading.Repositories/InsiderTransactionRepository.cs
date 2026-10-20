using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Repositories;

public class InsiderTransactionRepository : BaseRepository<InsiderTransaction>
{
    public InsiderTransactionRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<InsiderTransaction> GetByIssuerId(Guid stock)
    {
        return GetAll().Where(t => t.EquityIssuerId == stock);
    }

    // Same stock filter as GetByStock, with the InsiderOwner navigation eagerly loaded for
    // callers that read insider fields (name/role) while ordering or rendering rows.
    public IQueryable<InsiderTransaction> GetByIssuerIdWithOwner(Guid stock)
    {
        return GetByIssuerId(stock).Include(t => t.InsiderOwner);
    }

    public IQueryable<InsiderTransaction> GetByIssuerId(Guid stock, DateOnly from, DateOnly to)
    {
        return GetAll()
            .Where(t =>
                t.EquityIssuerId == stock && t.TransactionDate >= from && t.TransactionDate <= to
            );
    }

    public IQueryable<InsiderTransaction> GetByOwner(InsiderOwner owner)
    {
        return GetAll().Where(t => t.InsiderOwnerId == owner.Id);
    }

    public IQueryable<InsiderTransaction> GetByOwnerIds(IEnumerable<Guid> ownerIds)
    {
        return GetAll().Where(t => ownerIds.Contains(t.InsiderOwnerId));
    }

    public IQueryable<InsiderTransaction> GetHistoryByIssuerId(Guid stock)
    {
        return GetAll().Where(t => t.EquityIssuerId == stock);
    }

    /// <summary>
    /// Every actual-share row of each insider's newest filing for the issuer, with the owner. A
    /// filing reports one closing balance per ownership bucket, so its last line alone understates
    /// an insider holding under several classes or vehicles. A row without an accession number
    /// degrades to the insider's single newest row, and the accession clause is correlated per
    /// insider, so a joint filing never leaks another owner's rows in.
    /// </summary>
    public IQueryable<InsiderTransaction> GetCurrentPositionRows(Guid stock)
    {
        // The pre-v6 "no securities owned" sentinels (SecurityKind Unknown) and derivative rows
        // are other balances, never the actual-share position.
        var byStock = GetByIssuerId(stock)
            .Where(t => t.SecurityKind == InsiderSecurityKind.NonDerivative);
        var currentByStock = byStock.OrderCurrentPositionFirst();
        return GetByIssuerIdWithOwner(stock)
            .Where(t => t.SecurityKind == InsiderSecurityKind.NonDerivative)
            .Where(t =>
                t.Id
                    == currentByStock
                        .Where(t2 => t2.InsiderOwnerId == t.InsiderOwnerId)
                        .Select(t2 => t2.Id)
                        .First()
                || (
                    t.AccessionNumber != null
                    && t.AccessionNumber
                        == currentByStock
                            .Where(t2 => t2.InsiderOwnerId == t.InsiderOwnerId)
                            .Select(t2 => t2.AccessionNumber)
                            .First()
                )
            );
    }

    /// <summary>
    /// The issuer's actual-share filings, one row per insider and accession, with the keys that
    /// order them newest first the way <see cref="InsiderTradingQueryOrderExtensions.OrderCurrentPositionFirst"/>
    /// orders their rows. Rows without an accession number cannot be grouped into a filing.
    /// </summary>
    public IQueryable<InsiderFilingKey> GetFilingKeys(Guid stock) =>
        GetByIssuerId(stock)
            .Where(t =>
                t.SecurityKind == InsiderSecurityKind.NonDerivative && t.AccessionNumber != null
            )
            .GroupBy(t => new { t.InsiderOwnerId, t.AccessionNumber })
            .Select(g => new InsiderFilingKey
            {
                InsiderOwnerId = g.Key.InsiderOwnerId,
                AccessionNumber = g.Key.AccessionNumber,
                TransactionDate = g.Max(t => t.TransactionDate),
                FilingDate = g.Max(t => t.FilingDate),
            });

    /// <summary>Every actual-share row of these filings, whichever insider filed them.</summary>
    public IQueryable<InsiderTransaction> GetFilingRows(
        Guid stock,
        IReadOnlyCollection<string> accessionNumbers
    ) =>
        GetByIssuerId(stock)
            .Where(t =>
                t.SecurityKind == InsiderSecurityKind.NonDerivative
                && t.AccessionNumber != null
                && accessionNumbers.Contains(t.AccessionNumber)
            );

    /// <summary>
    /// Rows from any amendment that restates transaction and/or holding sections
    /// of the original report an owner filed for this company on
    /// <paramref name="originalFilingDate"/> (the filer-entered
    /// <c>dateOfOriginalSubmission</c>, stable across chained amendments), within
    /// the same authoritative Form 3/4/5 family.
    /// </summary>
    public IQueryable<InsiderTransaction> GetAmendmentsOfOriginal(
        InsiderOwner owner,
        Guid commonStockId,
        DateOnly originalFilingDate,
        InsiderOwnershipForm filingForm
    )
    {
        return GetAll()
            .Where(t =>
                t.InsiderOwnerId == owner.Id
                && t.EquityIssuerId == commonStockId
                && t.FilingForm == filingForm
                && t.OriginalFilingDate == originalFilingDate
                && t.TransactionCode != TransactionCode.IngestMarker
                && !(
                    t.TransactionCode == TransactionCode.Other
                    && t.SecurityTitle == "No Securities Owned"
                    && t.Shares == 0
                )
            );
    }

    /// <summary>
    /// Rows whose amendment has claimed <paramref name="accessionNumber"/> as the
    /// original it replaces — the durable guard that stops a late-arriving (or
    /// re-listed) original from re-inserting rows its amendment already superseded.
    /// </summary>
    public IQueryable<InsiderTransaction> GetAmendmentsClaiming(
        string accessionNumber,
        InsiderOwnershipForm filingForm
    )
    {
        return GetAll()
            .Where(t =>
                t.SupersededAccessionNumber == accessionNumber
                && t.FilingForm == filingForm
                && t.TransactionCode != TransactionCode.IngestMarker
                && !(
                    t.TransactionCode == TransactionCode.Other
                    && t.SecurityTitle == "No Securities Owned"
                    && t.Shares == 0
                )
            );
    }

    /// <summary>
    /// Amendment rows for this owner+company that have not yet resolved which
    /// original they replace, whose filer-entered original date falls in
    /// [<paramref name="windowStart"/>, <paramref name="windowEnd"/>]. The window
    /// absorbs EDGAR's date shift: an after-17:30 submission is indexed the next
    /// business day, so the feed FilingDate of the original can trail the
    /// filer-entered <c>dateOfOriginalSubmission</c> by up to a weekend. Matching
    /// is restricted to the incoming original's Form 3/4/5 family.
    /// </summary>
    public IQueryable<InsiderTransaction> GetUnresolvedAmendments(
        InsiderOwner owner,
        Guid commonStockId,
        DateOnly windowStart,
        DateOnly windowEnd,
        InsiderOwnershipForm filingForm
    )
    {
        return GetAll()
            .Where(t =>
                t.InsiderOwnerId == owner.Id
                && t.EquityIssuerId == commonStockId
                && t.FilingForm == filingForm
                && t.IsAmendment
                && t.TransactionCode != TransactionCode.IngestMarker
                && !(
                    t.TransactionCode == TransactionCode.Other
                    && t.SecurityTitle == "No Securities Owned"
                    && t.Shares == 0
                )
                && t.SupersededAccessionNumber == null
                && t.OriginalFilingDate != null
                && t.OriginalFilingDate >= windowStart
                && t.OriginalFilingDate <= windowEnd
            );
    }

    /// <summary>
    /// Candidate original rows an incoming amendment may supersede: the owner's
    /// non-amendment rows for this company filed in
    /// [<paramref name="windowStart"/>, <paramref name="windowEnd"/>] (the
    /// filer-entered original date plus the EDGAR indexing shift). The caller
    /// resolves the single original accession from these before deleting anything.
    /// Candidates from another Form 3/4/5 family are never returned.
    /// </summary>
    public IQueryable<InsiderTransaction> GetOriginalCandidates(
        InsiderOwner owner,
        Guid commonStockId,
        DateOnly windowStart,
        DateOnly windowEnd,
        InsiderOwnershipForm filingForm
    )
    {
        return GetAll()
            .Where(t =>
                t.InsiderOwnerId == owner.Id
                && t.EquityIssuerId == commonStockId
                && t.FilingForm == filingForm
                && !t.IsAmendment
                && t.TransactionCode != TransactionCode.IngestMarker
                && !(
                    t.TransactionCode == TransactionCode.Other
                    && t.SecurityTitle == "No Securities Owned"
                    && t.Shares == 0
                )
                && t.FilingDate >= windowStart
                && t.FilingDate <= windowEnd
            );
    }

    public IQueryable<InsiderTransaction> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(t => t.AccessionNumber == accessionNumber);
    }

    public IQueryable<InsiderTransaction> GetRecentByType(TransactionCode code, DateOnly since)
    {
        return GetAll().Where(t => t.TransactionCode == code && t.TransactionDate >= since);
    }

    public IQueryable<InsiderTransaction> GetRecent(DateOnly since)
    {
        return GetAll().Where(t => t.TransactionDate >= since);
    }
}
