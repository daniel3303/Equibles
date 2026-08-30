using Equibles.Core.AutoWiring;
using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.BusinessLogic.Search;

[Service(ServiceLifetime.Scoped, typeof(ISecDocumentService))]
public class SecDocumentService : ISecDocumentService
{
    private readonly DocumentRepository _documentRepository;
    private readonly ILogger<SecDocumentService> _logger;

    public SecDocumentService(
        DocumentRepository documentRepository,
        ILogger<SecDocumentService> logger
    )
    {
        _documentRepository = documentRepository;
        _logger = logger;
    }

    public async Task<List<SecDocumentInfo>> GetRecentDocuments(
        string ticker = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int maxItems = 10,
        int page = 1,
        DocumentType documentType = null,
        string itemNumber = null
    )
    {
        try
        {
            if (page < 1)
                throw new ArgumentOutOfRangeException(nameof(page), "Page must be at least 1.");
            if (maxItems < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(maxItems),
                    "Maximum items must be at least 1."
                );

            var offset = ((long)page - 1) * maxItems;
            if (offset > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(page), "Page offset is too large.");

            var documents = await BuildDocumentsQuery(
                    ticker,
                    startDate,
                    endDate,
                    documentType,
                    itemNumber
                )
                .OrderByDescending(d => d.ReportingDate)
                .ThenByDescending(d => d.Id)
                .Skip((int)offset)
                .Take(maxItems)
                .Select(d => new SecDocumentInfo
                {
                    Id = d.Id,
                    Ticker = d.CommonStock.Ticker,
                    CompanyName = d.CommonStock.Name,
                    DocumentType = d.DocumentType,
                    ReportingDate = d.ReportingDate,
                    ReportingForDate = d.ReportingForDate,
                    LineCount = d.LineCount,
                    Items = d.Items,
                })
                .ToListAsync();

            _logger.LogInformation(
                "Found {Count} recent documents for ticker filter {Ticker}",
                documents.Count,
                ticker
            );
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving recent documents for ticker filter {Ticker}",
                ticker
            );
            throw;
        }
    }

    public async Task<int> CountDocuments(
        string ticker = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        DocumentType documentType = null,
        string itemNumber = null
    )
    {
        return await BuildDocumentsQuery(ticker, startDate, endDate, documentType, itemNumber)
            .CountAsync();
    }

    private IQueryable<Document> BuildDocumentsQuery(
        string ticker,
        DateTime? startDate,
        DateTime? endDate,
        DocumentType documentType,
        string itemNumber
    )
    {
        var query = string.IsNullOrWhiteSpace(ticker)
            ? _documentRepository.GetAll()
            : _documentRepository.GetByTicker(ticker);

        if (startDate.HasValue)
        {
            var startDateOnly = DateOnly.FromDateTime(startDate.Value);
            query = query.Where(d => d.ReportingDate >= startDateOnly);
        }

        if (endDate.HasValue)
        {
            var endDateOnly = DateOnly.FromDateTime(endDate.Value);
            query = query.Where(d => d.ReportingDate <= endDateOnly);
        }

        if (documentType != null)
        {
            query = query.Where(d => d.DocumentType == documentType);
        }
        else
        {
            // No explicit type filter: honor DocumentType.HiddenFromFilingLists — types registered
            // as hidden (e.g. investor-relations news) are news-like content, not filings, and must
            // not crowd real filings out of the recent-documents list. They stay reachable through
            // search and through an explicit documentType request.
            var hiddenTypes = DocumentType.GetAll().Where(t => t.HiddenFromFilingLists).ToList();
            if (hiddenTypes.Count > 0)
            {
                query = query.Where(d => !hiddenTypes.Contains(d.DocumentType));
            }
        }

        if (itemNumber != null)
        {
            var first = itemNumber + ",";
            var middle = "," + itemNumber + ",";
            var last = "," + itemNumber;
            query = query.Where(d =>
                d.Items == itemNumber
                || d.Items.StartsWith(first)
                || d.Items.Contains(middle)
                || d.Items.EndsWith(last)
            );
        }

        return query;
    }
}
