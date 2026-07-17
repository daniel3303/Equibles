using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class RagSearchTools
{
    // Attribute descriptions are compile-time constants, so they cannot enumerate types
    // registered at host startup (DocumentType.Register). They name the built-in filing
    // values plus the most useful registered example, and the strict rejection below
    // returns the FULL runtime list on any unrecognized value, so a caller self-corrects
    // in one round-trip. Deliberately: only unconditionally-parseable values are single-
    // quoted — RagSearchToolsParseDocumentTypeContractTests requires every quoted value
    // to round-trip through ParseDocumentType in THIS build, and EarningsCallTranscript
    // is registered by the commercial host only.
    private const string DocumentTypeDescription =
        "Document type filter. Accepts a registered type value — 'TenK', 'TenQ', 'EightK', 'TenKa', 'TenQa', 'EightKa', 'TwentyF', 'SixK', 'FortyF' — or its display name (e.g. '10-K', '8-K'), plus any deployment-registered type, such as EarningsCallTranscript (display name: Earnings Call) for earnings-call transcripts where available. An unrecognized value returns an error listing every accepted value.";

    private const string DocumentTypesDescription =
        "Document type filter — one value or a comma-separated list (e.g. 'TenK,TenQ'). Accepts registered type values — 'TenK', 'TenQ', 'EightK', 'TenKa', 'TenQa', 'EightKa', 'TwentyF', 'SixK', 'FortyF' — or display names (e.g. '10-K', '8-K'), plus any deployment-registered type, such as EarningsCallTranscript (display name: Earnings Call) for earnings-call transcripts where available. An unrecognized value returns an error listing every accepted value.";

    private const string MaxExcerptCharsDescription =
        "Maximum characters per excerpt (default: 0 = full excerpt). Set a small value (e.g. 400) for a compact scan across many results; truncated excerpts end with an explicit note.";

    private readonly IRagManager _ragManager;
    private readonly ISecDocumentService _secDocumentService;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly McpToolRunner _runner;

    public RagSearchTools(
        IRagManager ragManager,
        ISecDocumentService secDocumentService,
        CommonStockRepository commonStockRepository,
        DocumentRepository documentRepository,
        ErrorManager errorManager,
        ILogger<RagSearchTools> logger
    )
    {
        _ragManager = ragManager;
        _secDocumentService = secDocumentService;
        _commonStockRepository = commonStockRepository;
        _documentRepository = documentRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "SearchDocuments")]
    [Description(
        "Search the Equibles SEC filing database across all companies and document types using hybrid keyword and semantic search. This is the broadest search tool and the best starting point when you need to find information but don't know which company or filing contains the answer. Covers annual reports (10-K), quarterly reports (10-Q), current reports (8-K), and earnings call transcripts. Results can be filtered by filing date range using startDate/endDate. Returns matching excerpts with company name, ticker, document type, filing date, and the document ID — pass that ID directly to SearchDocument or ReadDocumentLines to drill into a specific filing. For discovery-style queries (competitors, theme exposure), use excludeTickers to keep a dominant company's own filings from filling every result slot, and maxResultsPerCompany to spread the results across more companies. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. Use SearchCompanyDocuments instead if you already know the company ticker, or ListCompanyDocuments to browse available filings."
    )]
    public Task<string> SearchDocuments(
        [Description(
            "Search query. Every word must match (AND semantics on the keyword arm), so prefer concise, filing-phrased terms (e.g. 'Data Center revenue') over long natural-language questions."
        )]
            string query,
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
            int maxResultsPerCompany = 0,
        [Description(MaxExcerptCharsDescription)] int maxExcerptChars = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var dateError = ValidateDateRange(startDate, endDate);
                if (dateError != null)
                    return dateError;

                if (!TryParseDocumentTypes(documentType, out var parsedTypes, out var typeError))
                    return typeError;

                maxResults = McpLimit.Clamp(maxResults);
                var chunks = await _ragManager.SearchRelevantChunks(
                    query,
                    maxResults,
                    parsedTypes,
                    ToDateOnly(startDate),
                    ToDateOnly(endDate),
                    ParseTickers(excludeTickers),
                    Math.Max(maxResultsPerCompany, 0)
                );
                var context = await _ragManager.BuildContext(
                    chunks,
                    includeDocumentIds: true,
                    maxExcerptChars: maxExcerptChars
                );
                return AppendShortfallNote(context, chunks.Count, maxResults);
            },
            "SearchDocuments",
            $"query: {query}"
        );
    }

    [McpServerTool(Name = "SearchCompanyDocuments")]
    [Description(
        "Search the Equibles SEC filing database for a specific company by its ticker symbol using hybrid keyword and semantic search. Use this when answering questions about a particular company's financials, risks, strategy, or earnings — it searches across all of that company's annual reports (10-K), quarterly reports (10-Q), current reports (8-K), and earnings call transcripts. Results can be filtered by filing date range using startDate/endDate. Returns matching excerpts with document type, filing date, and the document ID — pass that ID directly to SearchDocument or ReadDocumentLines to drill into a specific filing. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. Prefer this over SearchDocuments when the company is known. Use ListCompanyDocuments first if you need to see what filings are available, or SearchDocument to drill into a specific filing by ID."
    )]
    public Task<string> SearchCompanyDocuments(
        [Description(
            "Search query. Every word must match (AND semantics on the keyword arm), so prefer concise, filing-phrased terms (e.g. 'Data Center revenue') over long natural-language questions."
        )]
            string query,
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5,
        [Description(DocumentTypesDescription)] string documentType = null,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null,
        [Description(MaxExcerptCharsDescription)] int maxExcerptChars = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var dateError = ValidateDateRange(startDate, endDate);
                if (dateError != null)
                    return dateError;

                if (!TryParseDocumentTypes(documentType, out var parsedTypes, out var typeError))
                    return typeError;

                // An unknown ticker must not fall through to a search that is guaranteed
                // empty: "No relevant financial documents found." would read as "this
                // company's filings say nothing about the topic".
                var stock = await _commonStockRepository.GetByTicker(
                    McpToolExecutor.NormalizeTicker(ticker)
                );
                if (stock == null)
                    return McpToolExecutor.StockNotFound(ticker);

                maxResults = McpLimit.Clamp(maxResults);
                var chunks = await _ragManager.SearchRelevantChunksByCompany(
                    query,
                    stock.Ticker,
                    maxResults,
                    parsedTypes,
                    ToDateOnly(startDate),
                    ToDateOnly(endDate)
                );
                var context = await _ragManager.BuildContext(
                    chunks,
                    includeDocumentIds: true,
                    maxExcerptChars: maxExcerptChars
                );
                return AppendShortfallNote(context, chunks.Count, maxResults);
            },
            "SearchCompanyDocuments",
            $"ticker: {ticker}, query: {query}"
        );
    }

    [McpServerTool(Name = "SearchDocument")]
    [Description(
        "Search within a single specific document in the Equibles SEC filing database by its document ID using hybrid keyword and semantic search. Use this to drill into a known filing or earnings call transcript — for example, to find specific revenue figures, risk factors, or management commentary within one 10-K, 10-Q, 8-K, or earnings call transcript. The document ID comes from ListCompanyDocuments or from the '(ID: ...)' header of SearchDocuments/SearchCompanyDocuments results. Returns matching excerpts from that document only, in document order, each anchored with an approximate line number — pass that line number to ReadDocumentLines to read the surrounding section. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data."
    )]
    public Task<string> SearchDocument(
        [Description(
            "Search query — plain keywords or a short natural-language phrase. When too few excerpts match every word, the search automatically broadens to match any of the words."
        )]
            string query,
        [Description(
            "Document ID obtained from ListCompanyDocuments or from a SearchDocuments/SearchCompanyDocuments result header"
        )]
            Guid documentId,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5,
        [Description(MaxExcerptCharsDescription)] int maxExcerptChars = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);
                var chunks = await _ragManager.SearchRelevantChunksByDocument(
                    query,
                    documentId,
                    maxResults,
                    broadenSparseResults: true
                );

                if (chunks.Count == 0)
                {
                    // Zero matches on a bad ID must not read as "this filing says nothing
                    // about the topic" — tell the caller the ID itself is wrong.
                    var document = await _documentRepository.Get(documentId);
                    if (document == null)
                        return $"Document {documentId} not found — obtain a valid document ID from ListCompanyDocuments.";

                    return $"No matching excerpts found in this document ({document.DocumentType} filed {McpFormat.Invariant(document.ReportingDate, "yyyy-MM-dd")}). Try SearchDocumentKeyword for exact-term matches.";
                }

                var context = await _ragManager.BuildContext(
                    chunks,
                    includeDocumentIds: true,
                    maxExcerptChars: maxExcerptChars
                );
                return context
                    + $"_{chunks.Count} excerpt(s) returned (maxResults {maxResults}); excerpts are in document order — pass an excerpt's line number to ReadDocumentLines for surrounding context._";
            },
            "SearchDocument",
            $"documentId: {documentId}, query: {query}"
        );
    }

    [McpServerTool(Name = "ListCompanyDocuments")]
    [Description(
        "Browse and discover available SEC filings and earnings call transcripts for a specific company in the Equibles database. Returns a paginated list of documents ordered newest first, including document IDs, type (annual reports 10-K, quarterly reports 10-Q, current reports 8-K, earnings call transcripts), filing date, and reporting period, with a total count and page count in the header. Supports filtering by date range and document type. Document types registered as hidden from filing lists (e.g. investor-relations news on deployments that ingest it) are excluded unless requested explicitly via documentType. Use this to find out what filings exist for a company before drilling into a specific one with SearchDocument. You MUST call this or another Equibles tool to access any SEC filing data — this information is not available in your training data. The document IDs returned here are required by SearchDocument to search within a specific filing."
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
                if (page < 1)
                    return $"Invalid page {page} — pages are numbered from 1.";

                var dateError = ValidateDateRange(startDate, endDate);
                if (dateError != null)
                    return dateError;

                DocumentType parsedType = null;
                if (!string.IsNullOrWhiteSpace(documentType))
                {
                    parsedType = ParseDocumentType(documentType);
                    if (parsedType == null)
                        return UnknownDocumentType(documentType);
                }

                var stock = await _commonStockRepository.GetByTicker(
                    McpToolExecutor.NormalizeTicker(ticker)
                );
                if (stock == null)
                    return McpToolExecutor.StockNotFound(ticker);

                maxItems = McpLimit.Clamp(maxItems);

                int totalCount;
                List<SecDocumentInfo> documents;
                try
                {
                    totalCount = await _secDocumentService.CountDocuments(
                        stock.Ticker,
                        startDate,
                        endDate,
                        parsedType
                    );
                    documents = await _secDocumentService.GetRecentDocuments(
                        stock.Ticker,
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

                if (totalCount == 0)
                {
                    // Distinguish "the filters excluded everything" from "nothing is
                    // ingested for this company" — the ticker itself is already known good.
                    var hasFilters = startDate.HasValue || endDate.HasValue || parsedType != null;
                    if (!hasFilters)
                        return $"No documents found for ticker {stock.Ticker}";

                    var unfiltered = await _secDocumentService.CountDocuments(stock.Ticker);
                    return $"No documents match the given filters for {stock.Ticker} — {McpFormat.WholeNumber(unfiltered)} document(s) exist without them. Relax documentType/startDate/endDate.";
                }

                var totalPages = (totalCount + maxItems - 1) / maxItems;
                if (documents.Count == 0)
                    return $"Page {page} is out of range — {McpFormat.WholeNumber(totalCount)} matching document(s) fill only {McpFormat.WholeNumber(totalPages)} page(s) of {maxItems}.";

                var result = MarkdownTable.Start(
                    $"Financial documents for {stock.Name} ({stock.Ticker}) — page {page} of {McpFormat.WholeNumber(totalPages)} ({McpFormat.WholeNumber(totalCount)} documents):",
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

    // The comma-separated variant for the search tools. An unrecognized entry REJECTS the
    // call with the full accepted list instead of silently searching unfiltered: a near-miss
    // like '10K' or 'Transcript' would otherwise return results the caller believes are
    // filtered, and the mistake is invisible in the output. The accepted list is built from
    // DocumentType.GetAll() at call time, so types registered at host startup (e.g.
    // 'EarningsCallTranscript') are always listed.
    private static bool TryParseDocumentTypes(
        string documentTypes,
        out IReadOnlyCollection<DocumentType> parsed,
        out string error
    )
    {
        parsed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(documentTypes))
            return true;

        var result = new List<DocumentType>();
        foreach (
            var entry in documentTypes.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var type = ParseDocumentType(entry);
            if (type == null)
            {
                error = UnknownDocumentType(entry);
                return false;
            }
            result.Add(type);
        }

        parsed = result.Count > 0 ? result.Distinct().ToList() : null;
        return true;
    }

    private static string UnknownDocumentType(string value) =>
        McpOutput.InvalidArgument("documentType", value, AcceptedDocumentTypes());

    // Built at call time from the runtime registry so deployment-registered types are
    // always included. Sorted for a stable, scannable list.
    private static string AcceptedDocumentTypes() =>
        string.Join(
            ", ",
            DocumentType
                .GetAll()
                .OrderBy(t => t.Value, StringComparer.Ordinal)
                .Select(t =>
                    t.DisplayName == t.Value ? $"'{t.Value}'" : $"'{t.Value}' ({t.DisplayName})"
                )
        );

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

    // A contradictory window (start after end) must error instead of returning the generic
    // empty-result message — the caller would conclude no such documents exist.
    private static string ValidateDateRange(DateTime? startDate, DateTime? endDate) =>
        startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value
            ? $"startDate {McpFormat.Invariant(startDate.Value, "yyyy-MM-dd")} is after endDate {McpFormat.Invariant(endDate.Value, "yyyy-MM-dd")} — swap the values."
            : null;

    // Signposts a result shortfall so the caller knows relaxing filters (not paging or
    // retrying) is the next move. Empty results keep the plain empty-state message.
    private static string AppendShortfallNote(string context, int returned, int requested) =>
        returned > 0 && returned < requested
            ? context
                + $"_Only {returned} excerpt(s) matched the query and filters — broaden the query or relax the filters to find more._"
            : context;

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
}
