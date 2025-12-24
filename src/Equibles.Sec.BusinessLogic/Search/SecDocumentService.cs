using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Core.AutoWiring;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace Equibles.Sec.BusinessLogic.Search;

[Service(ServiceLifetime.Scoped, typeof(ISecDocumentService))]
public class SecDocumentService : ISecDocumentService {
    private readonly DocumentRepository _documentRepository;
    private readonly ILogger<SecDocumentService> _logger;

    public SecDocumentService(DocumentRepository documentRepository, ILogger<SecDocumentService> logger) {
        _documentRepository = documentRepository;
        _logger = logger;
    }

    public async Task<List<SecDocumentInfo>> GetRecentDocuments(string ticker, DateTime? startDate = null, DateTime? endDate = null, int maxItems = 10, int page = 1, DocumentType documentType = null) {
        if (ticker == null) {
            throw new ApplicationException("Ticker cannot be null");
        }
        try {
            var query = _documentRepository.GetByTicker(ticker);

            if (startDate.HasValue) {
                var startDateOnly = DateOnly.FromDateTime(startDate.Value);
                query = query.Where(d => d.ReportingDate >= startDateOnly);
            }

            if (endDate.HasValue) {
                var endDateOnly = DateOnly.FromDateTime(endDate.Value);
                query = query.Where(d => d.ReportingDate <= endDateOnly);
            }

            if (documentType != null) {
                query = query.Where(d => d.DocumentType == documentType);
            }

            var documents = await query
                .OrderByDescending(d => d.ReportingDate)
                .Skip((page - 1) * maxItems)
                .Take(maxItems)
                .Select(d => new SecDocumentInfo {
                    Id = d.Id,
                    Ticker = d.CommonStock.Ticker,
                    CompanyName = d.CommonStock.Name,
                    DocumentType = d.DocumentType,
                    ReportingDate = d.ReportingDate,
                    ReportingForDate = d.ReportingForDate,
                    LineCount = d.LineCount
                })
                .ToListAsync();

            _logger.LogInformation("Found {Count} recent documents for ticker {Ticker}", documents.Count, ticker);
            return documents;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving recent documents for ticker {Ticker}", ticker);
            throw;
        }
    }
}
