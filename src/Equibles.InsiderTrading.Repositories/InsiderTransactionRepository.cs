using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Repositories;

public class InsiderTransactionRepository : BaseRepository<InsiderTransaction>
{
    public InsiderTransactionRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<InsiderTransaction> GetByStock(CommonStock stock)
    {
        return GetAll().Where(t => t.CommonStockId == stock.Id);
    }

    // Same stock filter as GetByStock, with the InsiderOwner navigation eagerly loaded for
    // callers that read insider fields (name/role) while ordering or rendering rows.
    public IQueryable<InsiderTransaction> GetByStockWithOwner(CommonStock stock)
    {
        return GetByStock(stock).Include(t => t.InsiderOwner);
    }

    public IQueryable<InsiderTransaction> GetByStock(CommonStock stock, DateOnly from, DateOnly to)
    {
        return GetAll()
            .Where(t =>
                t.CommonStockId == stock.Id && t.TransactionDate >= from && t.TransactionDate <= to
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

    public IQueryable<InsiderTransaction> GetHistoryByStock(CommonStock stock)
    {
        return GetAll().Where(t => t.CommonStockId == stock.Id);
    }

    /// <summary>
    /// Rows from any amendment that restates the original report an owner filed for
    /// this company on <paramref name="originalFilingDate"/> (the filer-entered
    /// <c>dateOfOriginalSubmission</c>, stable across chained amendments).
    /// </summary>
    public IQueryable<InsiderTransaction> GetAmendmentsOfOriginal(
        InsiderOwner owner,
        Guid commonStockId,
        DateOnly originalFilingDate
    )
    {
        return GetAll()
            .Where(t =>
                t.InsiderOwnerId == owner.Id
                && t.CommonStockId == commonStockId
                && t.OriginalFilingDate == originalFilingDate
            );
    }

    /// <summary>
    /// Rows whose amendment has claimed <paramref name="accessionNumber"/> as the
    /// original it replaces — the durable guard that stops a late-arriving (or
    /// re-listed) original from re-inserting rows its amendment already superseded.
    /// </summary>
    public IQueryable<InsiderTransaction> GetAmendmentsClaiming(string accessionNumber)
    {
        return GetAll().Where(t => t.SupersededAccessionNumber == accessionNumber);
    }

    /// <summary>
    /// Amendment rows for this owner+company that have not yet resolved which
    /// original they replace, whose filer-entered original date falls in
    /// [<paramref name="windowStart"/>, <paramref name="windowEnd"/>]. The window
    /// absorbs EDGAR's date shift: an after-17:30 submission is indexed the next
    /// business day, so the feed FilingDate of the original can trail the
    /// filer-entered <c>dateOfOriginalSubmission</c> by up to a weekend.
    /// </summary>
    public IQueryable<InsiderTransaction> GetUnresolvedAmendments(
        InsiderOwner owner,
        Guid commonStockId,
        DateOnly windowStart,
        DateOnly windowEnd
    )
    {
        return GetAll()
            .Where(t =>
                t.InsiderOwnerId == owner.Id
                && t.CommonStockId == commonStockId
                && t.IsAmendment
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
    /// </summary>
    public IQueryable<InsiderTransaction> GetOriginalCandidates(
        InsiderOwner owner,
        Guid commonStockId,
        DateOnly windowStart,
        DateOnly windowEnd
    )
    {
        return GetAll()
            .Where(t =>
                t.InsiderOwnerId == owner.Id
                && t.CommonStockId == commonStockId
                && !t.IsAmendment
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
