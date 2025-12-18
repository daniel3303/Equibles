using Equibles.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class DocumentRepository : BaseRepository<Document> {
    public DocumentRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public async Task<bool> Exists(CommonStock company, DocumentType documentType, DateOnly reportingDate,
        DateOnly reportingForDate
    ) {
        return await GetAll().AnyAsync(d =>
            d.CommonStock == company &&
            d.DocumentType == documentType &&
            d.ReportingDate == reportingDate &&
            d.ReportingForDate == reportingForDate);
    }

    public IQueryable<Document> GetByCompany(CommonStock company) {
        return GetAll().Where(d => d.CommonStock == company);
    }

    public IQueryable<Document> GetByTicker(string ticker) {
        return GetAll().Where(d =>
            d.CommonStock.Ticker.ToLower() == ticker.ToLower()
            || d.CommonStock.SecondaryTickers.Contains(ticker.ToUpper()));
    }

    public IQueryable<Document> GetByDocumentType(DocumentType documentType) {
        return GetAll().Where(d => d.DocumentType == documentType);
    }

    public async Task<Document> GetWithContent(Guid id) {
        return await GetAll()
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public IQueryable<Document> GetByDateRange(DateOnly? fromDate = null, DateOnly? toDate = null) {
        var query = GetAll();

        if (fromDate.HasValue)
            query = query.Where(d => d.ReportingDate >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(d => d.ReportingDate <= toDate.Value);

        return query;
    }
}