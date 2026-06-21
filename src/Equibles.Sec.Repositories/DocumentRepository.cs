using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class DocumentRepository : BaseRepository<Document>
{
    public DocumentRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<bool> Exists(
        CommonStock company,
        DocumentType documentType,
        DateOnly reportingDate,
        DateOnly reportingForDate
    )
    {
        return await GetAll()
            .AnyAsync(d =>
                d.CommonStock == company
                && d.DocumentType == documentType
                && d.ReportingDate == reportingDate
                && d.ReportingForDate == reportingForDate
            );
    }

    public IQueryable<Document> GetByCompany(CommonStock company)
    {
        return GetAll().Where(d => d.CommonStock == company);
    }

    public IQueryable<Document> GetByTicker(string ticker)
    {
        return GetAll()
            .Where(d =>
                d.CommonStock.Ticker.ToLower() == ticker.ToLower()
                || d.CommonStock.SecondaryTickers.Contains(ticker.ToUpper())
            );
    }

    public IQueryable<Document> GetByDocumentType(DocumentType documentType)
    {
        return GetAll().Where(d => d.DocumentType == documentType);
    }

    public IQueryable<Document> GetByXbrlStatus(XbrlCaptureStatus status)
    {
        return GetAll().Where(d => d.XbrlStatus == status);
    }

    /// <summary>
    /// The XBRL backfill work-set: the <see cref="XbrlCaptureStatus.NotChecked"/> documents the
    /// backfill will actually select. A document qualifies only when it came from an EDGAR filing
    /// — the accession is stored directly, or recoverable from the stored EDGAR submission URL
    /// (rows ingested before AccessionNumber existed) — and its issuer has a CIK. Non-EDGAR
    /// documents (e.g. earnings-call transcripts) are NotChecked but have no filing to re-fetch,
    /// so they are never selected and must not count as pending work. Documents that have
    /// exhausted their retry ceiling are excluded too, since they can no longer be reselected.
    /// This is the single definition of "pending backfill" shared by the worker and the dashboard.
    /// </summary>
    public IQueryable<Document> GetPendingXbrlBackfill()
    {
        return GetByXbrlStatus(XbrlCaptureStatus.NotChecked)
            .Where(d =>
                (
                    d.AccessionNumber != null
                    || (d.SourceUrl != null && d.SourceUrl.Contains("/Archives/edgar/data/"))
                )
                && d.CommonStock.Cik != null
                && d.XbrlCaptureAttempts < Document.MaxXbrlCaptureAttempts
            );
    }

    public async Task<Document> GetWithContent(Guid id)
    {
        // The content bytes ride along eagerly: leaving File.FileContent to a lazy load
        // lets an aborted or transient load mid-request corrupt the navigation's loaded
        // state (the reference is non-null by construction) and crash the page instead
        // of rendering the document.
        return await GetAll()
            .Include(d => d.Content)
                .ThenInclude(f => f.FileContent)
            .Include(d => d.CommonStock)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public IQueryable<Document> GetByDateRange(DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        var query = GetAll();

        if (fromDate.HasValue)
            query = query.Where(d => d.ReportingDate >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(d => d.ReportingDate <= toDate.Value);

        return query;
    }
}
