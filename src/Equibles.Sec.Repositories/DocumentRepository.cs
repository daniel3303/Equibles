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

    public IQueryable<Document> GetByReportedStatementsStatus(XbrlCaptureStatus status)
    {
        return GetAll().Where(d => d.ReportedStatementsStatus == status);
    }

    /// <summary>
    /// The as-filed HTML backfill work-set: EDGAR-sourced 8-K documents whose stitched as-filed
    /// HTML is below the current builder version (<see cref="Document.AsFiledHtmlBuilderVersion"/>)
    /// and still under the retry ceiling. Scoped to 8-Ks because that's where the linked exhibits
    /// (the Exhibit 99.1 press release) and the broken citations live; widen the type set to extend
    /// coverage. A document qualifies only when it came from an EDGAR filing (the accession is
    /// stored or recoverable from the submission URL) and its issuer has a CIK, so the backfill
    /// can re-fetch the submission to stitch. This is the single definition of "pending as-filed
    /// HTML" shared by the worker and the backoffice dashboard metric.
    /// </summary>
    public IQueryable<Document> GetPendingAsFiledHtml()
    {
        return GetAll()
            .Where(d =>
                (d.DocumentType == DocumentType.EightK || d.DocumentType == DocumentType.EightKa)
                && d.AsFiledHtmlVersion < Document.AsFiledHtmlBuilderVersion
                && d.AsFiledHtmlAttempts < Document.MaxAsFiledHtmlAttempts
                && (
                    d.AccessionNumber != null
                    || (d.SourceUrl != null && d.SourceUrl.Contains("/Archives/edgar/data/"))
                )
                && d.CommonStock.Cik != null
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
