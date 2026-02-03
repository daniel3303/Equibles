using System.ComponentModel;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Core.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Sec.Data.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class RagSearchTools {
    private const string DocumentTypeDescription =
        "Document type filter. Allowed values: 'TenK', 'TenQ', 'EightK', 'TenKa', 'TenQa', 'EightKa', 'TwentyF', 'SixK', 'FortyF', 'EarningsCallTranscript'";

    private readonly IRagManager _ragManager;
    private readonly ISecDocumentService _secDocumentService;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<RagSearchTools> _logger;

    public RagSearchTools(IRagManager ragManager, ISecDocumentService secDocumentService,
        ErrorManager errorManager, ILogger<RagSearchTools> logger) {
        _ragManager = ragManager;
        _secDocumentService = secDocumentService;
        _errorManager = errorManager;
        _logger = logger;
    }

    [McpServerTool(Name = "SearchDocuments")]
    [Description("Search the Equibles SEC filing database across all companies and document types using semantic vector search. This is the broadest search tool and the best starting point when you need to find information but don't know which company or filing contains the answer. Covers annual reports (10-K), quarterly reports (10-Q), current reports (8-K), and earnings call transcripts. Results can be filtered by filing date range using startDate/endDate. Returns matching excerpts with company name, ticker, document type, and filing date. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. Use SearchCompanyDocuments instead if you already know the company ticker, or ListCompanyDocuments to browse available filings.")]
    public async Task<string> SearchDocuments(
        [Description("Natural language search query")] string query,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5,
        [Description(DocumentTypeDescription)] string documentType = null,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null
    ) {
        try {
            var parsedType = ParseDocumentType(documentType);
            var chunks = await _ragManager.SearchRelevantChunks(query, maxResults, parsedType,
                startDate.HasValue ? DateOnly.FromDateTime(startDate.Value) : null,
                endDate.HasValue ? DateOnly.FromDateTime(endDate.Value) : null);
            return await _ragManager.BuildContext(chunks);
        } catch (Exception ex) {
            _logger.LogError(ex, "SearchDocuments failed for query {Query}", query);
            try { await _errorManager.Create(ErrorSource.McpTool, "SearchDocuments", ex.Message, ex.StackTrace, $"query: {query}"); } catch { }
            return "An error occurred while searching documents. Please try again.";
        }
    }

    [McpServerTool(Name = "SearchCompanyDocuments")]
    [Description("Search the Equibles SEC filing database for a specific company by its ticker symbol using semantic vector search. Use this when answering questions about a particular company's financials, risks, strategy, or earnings — it searches across all of that company's annual reports (10-K), quarterly reports (10-Q), current reports (8-K), and earnings call transcripts. Results can be filtered by filing date range using startDate/endDate. Returns matching excerpts with document type and filing date. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. Prefer this over SearchDocuments when the company is known. Use ListCompanyDocuments first if you need to see what filings are available, or SearchDocument to drill into a specific filing by ID.")]
    public async Task<string> SearchCompanyDocuments(
        [Description("Natural language search query")] string query,
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5,
        [Description(DocumentTypeDescription)] string documentType = null,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null
    ) {
        try {
            var parsedType = ParseDocumentType(documentType);
            var chunks = await _ragManager.SearchRelevantChunksByCompany(query, ticker, maxResults, parsedType,
                startDate.HasValue ? DateOnly.FromDateTime(startDate.Value) : null,
                endDate.HasValue ? DateOnly.FromDateTime(endDate.Value) : null);
            return await _ragManager.BuildContext(chunks);
        } catch (Exception ex) {
            _logger.LogError(ex, "SearchCompanyDocuments failed for {Ticker} query {Query}", ticker, query);
            try { await _errorManager.Create(ErrorSource.McpTool, "SearchCompanyDocuments", ex.Message, ex.StackTrace, $"ticker: {ticker}, query: {query}"); } catch { }
            return "An error occurred while searching company documents. Please try again.";
        }
    }

    [McpServerTool(Name = "SearchDocument")]
    [Description("Search within a single specific document in the Equibles SEC filing database by its document ID using semantic vector search. Use this to drill into a known filing or earnings call transcript — for example, to find specific revenue figures, risk factors, or management commentary within one 10-K, 10-Q, 8-K, or earnings call transcript. The document ID must be obtained first by calling ListCompanyDocuments. Returns matching excerpts from that document only. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. Typical workflow: call ListCompanyDocuments to find the filing, then call SearchDocument with the returned document ID to extract specific information.")]
    public async Task<string> SearchDocument(
        [Description("Natural language search query")] string query,
        [Description("Document ID obtained from ListCompanyDocuments")] Guid documentId,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5
    ) {
        try {
            var chunks = await _ragManager.SearchRelevantChunksByDocument(query, documentId, maxResults);
            return await _ragManager.BuildContext(chunks);
        } catch (Exception ex) {
            _logger.LogError(ex, "SearchDocument failed for document {DocumentId} query {Query}", documentId, query);
            try { await _errorManager.Create(ErrorSource.McpTool, "SearchDocument", ex.Message, ex.StackTrace, $"documentId: {documentId}, query: {query}"); } catch { }
            return "An error occurred while searching the document. Please try again.";
        }
    }

    [McpServerTool(Name = "ListCompanyDocuments")]
    [Description("Browse and discover available SEC filings and earnings call transcripts for a specific company in the Equibles database. Returns a paginated list of documents ordered newest first, including document IDs, type (annual reports 10-K, quarterly reports 10-Q, current reports 8-K, earnings call transcripts), filing date, and reporting period. Supports filtering by date range and document type. Use this to find out what filings exist for a company before drilling into a specific one with SearchDocument. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. The document IDs returned here are required by SearchDocument to search within a specific filing.")]
    public async Task<string> ListCompanyDocuments(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Page number for pagination (default: 1)")] int page = 1,
        [Description("Maximum number of documents per page (default: 10)")] int maxItems = 10,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null,
        [Description(DocumentTypeDescription)] string documentType = null
    ) {
        try {
            var parsedType = ParseDocumentType(documentType);
            List<SecDocumentInfo> documents;

            try {
                documents = await _secDocumentService.GetRecentDocuments(ticker, startDate, endDate, maxItems, page, parsedType);
            } catch (ApplicationException ex) {
                return ex.Message;
            }

            if (documents.Count == 0) {
                return $"No documents found for ticker {ticker}";
            }

            var result = new StringBuilder();
            result.AppendLine($"Financial documents for {documents.First().CompanyName} ({ticker}) — page {page}:");
            result.AppendLine();
            result.AppendLine("ID | Type | Filed | Reporting For | Lines");
            result.AppendLine("---|------|-------|---------------|------");

            foreach (var doc in documents) {
                result.AppendLine(
                    $"{doc.Id} | {doc.DocumentType} | {doc.ReportingDate:yyyy-MM-dd} | {doc.ReportingForDate:yyyy-MM-dd} | {doc.LineCount:N0}");
            }

            return result.ToString();
        } catch (Exception ex) {
            _logger.LogError(ex, "ListCompanyDocuments failed for {Ticker}", ticker);
            try { await _errorManager.Create(ErrorSource.McpTool, "ListCompanyDocuments", ex.Message, ex.StackTrace, $"ticker: {ticker}"); } catch { }
            return "An error occurred while listing company documents. Please try again.";
        }
    }

    private static DocumentType ParseDocumentType(string documentType) {
        if (string.IsNullOrWhiteSpace(documentType))
            return null;

        return DocumentType.FromDisplayName(documentType)
               ?? DocumentType.FromValue(documentType);
    }
}
