using System.ComponentModel;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Sec.Data.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class RagSearchTools
{
    private const string DocumentTypeDescription =
        "Document type filter. Allowed values: 'TenK', 'TenQ', 'EightK', 'TenKa', 'TenQa', 'EightKa', 'TwentyF', 'SixK', 'FortyF'";

    private const string DocumentTypesDescription =
        "Document type filter — one value or a comma-separated list (e.g. 'TenK,TenQ'). Allowed values: 'TenK', 'TenQ', 'EightK', 'TenKa', 'TenQa', 'EightKa', 'TwentyF', 'SixK', 'FortyF'";

    private readonly IRagManager _ragManager;
    private readonly ISecDocumentService _secDocumentService;
    private readonly McpToolRunner _runner;

    public RagSearchTools(
        IRagManager ragManager,
        ISecDocumentService secDocumentService,
        ErrorManager errorManager,
        ILogger<RagSearchTools> logger
    )
    {
        _ragManager = ragManager;
        _secDocumentService = secDocumentService;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "SearchDocuments")]
    [Description(
        "Search the Equibles SEC filing database across all companies and document types using hybrid keyword and semantic search. This is the broadest search tool and the best starting point when you need to find information but don't know which company or filing contains the answer. Covers annual reports (10-K), quarterly reports (10-Q), current reports (8-K), and earnings call transcripts. Results can be filtered by filing date range using startDate/endDate. Returns matching excerpts with company name, ticker, document type, and filing date. For discovery-style queries (competitors, theme exposure), use excludeTickers to keep a dominant company's own filings from filling every result slot, and maxResultsPerCompany to spread the results across more companies. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. Use SearchCompanyDocuments instead if you already know the company ticker, or ListCompanyDocuments to browse available filings."
    )]
    public Task<string> SearchDocuments(
        [Description("Natural language search query")] string query,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5,
        [Description(DocumentTypesDescription)] string documentType = null,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null,
        [Description(
            "Tickers whose filings are excluded from the results — one value or a comma-separated list (e.g. 'AAPL,MSFT'). Use when a company's own filings would dominate the results for a query about its market."
        )]
            string excludeTickers = null,
        [Description(
            "Maximum results from any single company (default: 0 = unlimited). Set a small value (e.g. 2) to spread results across more companies for discovery-style queries."
        )]
            int maxResultsPerCompany = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);
                var chunks = await _ragManager.SearchRelevantChunks(
                    query,
                    maxResults,
                    ParseDocumentTypes(documentType),
                    ToDateOnly(startDate),
                    ToDateOnly(endDate),
                    ParseTickers(excludeTickers),
                    Math.Max(maxResultsPerCompany, 0)
                );
                return await _ragManager.BuildContext(chunks);
            },
            "SearchDocuments",
            $"query: {query}"
        );
    }

    [McpServerTool(Name = "SearchCompanyDocuments")]
    [Description(
        "Search the Equibles SEC filing database for a specific company by its ticker symbol using hybrid keyword and semantic search. Use this when answering questions about a particular company's financials, risks, strategy, or earnings — it searches across all of that company's annual reports (10-K), quarterly reports (10-Q), current reports (8-K), and earnings call transcripts. Results can be filtered by filing date range using startDate/endDate. Returns matching excerpts with document type and filing date. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. Prefer this over SearchDocuments when the company is known. Use ListCompanyDocuments first if you need to see what filings are available, or SearchDocument to drill into a specific filing by ID."
    )]
    public Task<string> SearchCompanyDocuments(
        [Description("Natural language search query")] string query,
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5,
        [Description(DocumentTypesDescription)] string documentType = null,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);
                var chunks = await _ragManager.SearchRelevantChunksByCompany(
                    query,
                    ticker,
                    maxResults,
                    ParseDocumentTypes(documentType),
                    ToDateOnly(startDate),
                    ToDateOnly(endDate)
                );
                return await _ragManager.BuildContext(chunks);
            },
            "SearchCompanyDocuments",
            $"ticker: {ticker}, query: {query}"
        );
    }

    [McpServerTool(Name = "SearchDocument")]
    [Description(
        "Search within a single specific document in the Equibles SEC filing database by its document ID using hybrid keyword and semantic search. Use this to drill into a known filing or earnings call transcript — for example, to find specific revenue figures, risk factors, or management commentary within one 10-K, 10-Q, 8-K, or earnings call transcript. The document ID must be obtained first by calling ListCompanyDocuments. Returns matching excerpts from that document only. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. Typical workflow: call ListCompanyDocuments to find the filing, then call SearchDocument with the returned document ID to extract specific information."
    )]
    public Task<string> SearchDocument(
        [Description("Natural language search query")] string query,
        [Description("Document ID obtained from ListCompanyDocuments")] Guid documentId,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);
                var chunks = await _ragManager.SearchRelevantChunksByDocument(
                    query,
                    documentId,
                    maxResults
                );
                return await _ragManager.BuildContext(chunks);
            },
            "SearchDocument",
            $"documentId: {documentId}, query: {query}"
        );
    }

    [McpServerTool(Name = "ListCompanyDocuments")]
    [Description(
        "Browse and discover available SEC filings and earnings call transcripts for a specific company in the Equibles database. Returns a paginated list of documents ordered newest first, including document IDs, type (annual reports 10-K, quarterly reports 10-Q, current reports 8-K, earnings call transcripts), filing date, and reporting period. Supports filtering by date range and document type. Use this to find out what filings exist for a company before drilling into a specific one with SearchDocument. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. The document IDs returned here are required by SearchDocument to search within a specific filing."
    )]
    public Task<string> ListCompanyDocuments(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Page number for pagination (default: 1)")] int page = 1,
        [Description("Maximum number of documents per page (default: 10)")] int maxItems = 10,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null,
        [Description(DocumentTypeDescription)] string documentType = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var parsedType = ParseDocumentType(documentType);
                List<SecDocumentInfo> documents;

                try
                {
                    documents = await _secDocumentService.GetRecentDocuments(
                        ticker,
                        startDate,
                        endDate,
                        maxItems,
                        page,
                        parsedType
                    );
                }
                catch (ApplicationException ex)
                {
                    return ex.Message;
                }

                if (documents.Count == 0)
                {
                    return $"No documents found for ticker {ticker}";
                }

                var result = MarkdownTable.Start(
                    $"Financial documents for {documents.First().CompanyName} ({ticker}) — page {page}:",
                    "ID | Type | Filed | Reporting For | Lines",
                    "---|------|-------|---------------|------"
                );

                result.AppendRows(
                    documents,
                    doc =>
                        $"{doc.Id} | {doc.DocumentType} | {doc.ReportingDate:yyyy-MM-dd} | {doc.ReportingForDate:yyyy-MM-dd} | {McpFormat.WholeNumber(doc.LineCount)}"
                );

                return result.ToString();
            },
            "ListCompanyDocuments",
            $"ticker: {ticker}"
        );
    }

    private static DocumentType ParseDocumentType(string documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            return null;

        return DocumentType.FromDisplayName(documentType) ?? DocumentType.FromValue(documentType);
    }

    // The comma-separated variant for the search tools. Unrecognized entries are
    // skipped rather than erroring, matching the single-type parameter's established
    // lenient behavior (an unknown value has always meant "no type filter"); null when
    // nothing usable remains.
    private static IReadOnlyCollection<DocumentType> ParseDocumentTypes(string documentTypes)
    {
        if (string.IsNullOrWhiteSpace(documentTypes))
            return null;

        var parsed = documentTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseDocumentType)
            .Where(type => type != null)
            .Distinct()
            .ToList();
        return parsed.Count > 0 ? parsed : null;
    }

    // Comma-separated tickers for the exclusion filter; null when nothing usable.
    private static IReadOnlyCollection<string> ParseTickers(string tickers)
    {
        if (string.IsNullOrWhiteSpace(tickers))
            return null;

        var parsed = tickers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parsed.Count > 0 ? parsed : null;
    }

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
}
