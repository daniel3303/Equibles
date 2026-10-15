using System.ComponentModel;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class RagSearchTools
{
    private const int MaxExcludedTickers = 25;

    // Attribute descriptions are compile-time constants, so they cannot enumerate types
    // registered at host startup (DocumentType.Register). They name the built-in filing
    // values plus the most useful registered example, and the strict rejection below
    // returns the FULL runtime list on any unrecognized value, so a caller self-corrects
    // in one round-trip. Deliberately: only unconditionally-parseable values are single-
    // quoted — RagSearchToolsParseDocumentTypeContractTests requires every quoted value
    // to round-trip through ParseDocumentType in THIS build, and EarningsCallTranscript
    // is registered by the commercial host only.
    private const string DocumentTypeDescription =
        "Document type filter: a registered value such as 'TenK', 'TenQa', 'EightK', 'TwentyF', 'Def14A', 'Defa14A', 'SThree' or 'TenderOfferThirdParty', or its SEC form name ('10-K', '20-F/A', 'DEFA14A', 'S-3ASR', 'SC TO-T', 'NT 10-Q'), plus deployment-registered types such as EarningsCallTranscript. An unrecognized value returns an error listing every accepted value.";

    private const string DocumentTypesDescription =
        "Optional document types. Accepts registered values such as TenK, TenQ, EightK, TwentyF, SixK, FortyF, Def14A, SOne, or deployment-registered types such as EarningsCallTranscript. SEC form names such as 10-K, DEFA14A, S-3, SC TO-T or NT 10-K are also accepted; an invalid value returns the full accepted list.";

    private const string MaxExcerptCharsDescription =
        "Maximum characters per excerpt (default: 0 = full excerpt). Set a small value (e.g. 400) for a compact scan across many results; truncated excerpts end with an explicit note.";

    private readonly IRagManager _ragManager;
    private readonly ISecDocumentService _secDocumentService;
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly IFileManager _fileManager;
    private readonly IDocumentExcerptLinkBuilder _excerptLinkBuilder;
    private readonly McpToolRunner _runner;

    // The excerpt link builder is optional by design — no framework registration exists,
    // and the container falls back to null on deployments without a public viewer.
    public RagSearchTools(
        IRagManager ragManager,
        ISecDocumentService secDocumentService,
        EquityIssuerRepository commonStockRepository,
        DocumentRepository documentRepository,
        IFileManager fileManager,
        ErrorManager errorManager,
        ILogger<RagSearchTools> logger,
        IDocumentExcerptLinkBuilder excerptLinkBuilder = null
    )
    {
        _ragManager = ragManager;
        _secDocumentService = secDocumentService;
        _commonStockRepository = commonStockRepository;
        _documentRepository = documentRepository;
        _fileManager = fileManager;
        _excerptLinkBuilder = excerptLinkBuilder;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "SearchDocuments", Title = "Search SEC Filings", ReadOnly = true)]
    [Description(
        "Search SEC filings and earnings-call transcripts with hybrid keyword and semantic retrieval, or with match 'phrase' for only the excerpts containing an exact phrase. Omit ticker to search every company, or provide one ticker to search only that company. Returns excerpts with document IDs for SearchDocument or ReadDocumentLines. Use excludeTickers and maxResultsPerCompany only for market-wide discovery; use ListFilings to browse filings newest first without a text query."
    )]
    public Task<string> SearchDocuments(
        [Description(
            "Search query — plain keywords or a short natural-language phrase. In ranked mode, when too few excerpts match every word, the search automatically broadens to match any of the words; concise, filing-phrased terms (e.g. 'Data Center revenue') still rank best. In phrase mode, the exact phrase to find."
        )]
            string query,
        [Description(
            "Optional company ticker; a listing outside the US is MIC:TICKER (e.g. XPAR:AIR). Omit to search across all companies."
        )]
            string ticker = null,
        [Description("Maximum number of results to return (default: 5, max: 500)")]
            int maxResults = 5,
        [Description(DocumentTypesDescription)] string[] documentTypes = null,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null,
        [Description(
            "Optional tickers to exclude from a market-wide search (max 25). Cannot be combined with ticker."
        )]
            string[] excludeTickers = null,
        [Description(
            "Maximum results from any single company (default: 0 = unlimited). Set a small value (e.g. 2) to spread results across more companies for discovery-style queries."
        )]
            int maxResultsPerCompany = 0,
        [Description(MaxExcerptCharsDescription)] int maxExcerptChars = 0,
        [Description(
            "How the query matches: 'ranked' (default — relevance-ranked keyword and semantic retrieval that broadens to any of the words when results are sparse) or 'phrase' (only excerpts containing the query's words consecutively and in order; case and punctuation are ignored)."
        )]
            string match = "ranked"
    )
    {
        return _runner.Execute(
            async () =>
            {
                var dateError = ValidateDateRange(startDate, endDate);
                if (dateError != null)
                    return dateError;

                if (!TryParseSearchMatch(match, out var searchMatch))
                    return $"Unknown match \"{match}\" — pass 'ranked' (default) or 'phrase'.";

                if (!TryParseDocumentTypes(documentTypes, out var parsedTypes, out var typeError))
                    return typeError;

                if (!TryParseTickers(excludeTickers, out var parsedTickers, out var tickerError))
                    return tickerError;

                maxResults = McpLimit.Clamp(maxResults);
                List<Chunk> chunks;
                if (!string.IsNullOrWhiteSpace(ticker))
                {
                    if (parsedTickers != null)
                        return "ticker and excludeTickers cannot be combined.";

                    var (_, filer, filerError) = await ResolveFiler(ticker);
                    if (filerError != null)
                        return filerError;

                    chunks = await SearchOrTimeoutFault(
                        () =>
                            _ragManager.SearchRelevantChunksByCompany(
                                query,
                                filer,
                                maxResults,
                                parsedTypes,
                                ToDateOnly(startDate),
                                ToDateOnly(endDate),
                                broadenSparseResults: true,
                                match: searchMatch
                            ),
                        scoped: true,
                        match: searchMatch
                    );
                }
                else
                {
                    chunks = await SearchOrTimeoutFault(
                        () =>
                            _ragManager.SearchRelevantChunks(
                                query,
                                maxResults,
                                parsedTypes,
                                ToDateOnly(startDate),
                                ToDateOnly(endDate),
                                parsedTickers,
                                Math.Max(maxResultsPerCompany, 0),
                                broadenSparseResults: true,
                                match: searchMatch
                            ),
                        scoped: false,
                        match: searchMatch
                    );
                }

                var context = await _ragManager.BuildContext(
                    chunks,
                    includeDocumentIds: true,
                    maxExcerptChars: maxExcerptChars,
                    includeExcerptLinks: true
                );
                return AppendShortfallNote(context, chunks.Count, maxResults);
            },
            "SearchDocuments",
            $"ticker: {ticker}, query: {query}"
        );
    }

    [McpServerTool(Name = "SearchDocument", Title = "Search Within One Filing", ReadOnly = true)]
    [Description(
        "Search one SEC filing or earnings-call transcript by document ID. semantic mode uses hybrid relevance and returns excerpts in document order with approximate line numbers. exact mode performs a literal case-insensitive substring match and returns precise matching lines. Get document IDs from SearchDocuments or ListFilings; use ReadDocumentLines for surrounding text."
    )]
    public Task<string> SearchDocument(
        [Description(
            "Search query — plain keywords or a short natural-language phrase. When too few excerpts match every word, the search automatically broadens to match any of the words. In searchMode 'exact', matched as a literal case-insensitive substring."
        )]
            string query,
        [Description("Document ID obtained from ListFilings or a SearchDocuments result header")]
            Guid documentId,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5,
        [Description(MaxExcerptCharsDescription)] int maxExcerptChars = 0,
        [Description(
            "How to match: 'semantic' (default — hybrid keyword and semantic relevance) or 'exact' (literal case-insensitive substring match with precise line numbers)."
        )]
            string searchMode = "semantic"
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                // Exact mode uses the same literal scan as the non-tool test seam, so a
                // caller can flip modes without switching tools. Unknown values get a
                // corrective error, never a silent semantic search the caller would misread
                // as exact-match results.
                var mode = (searchMode ?? "semantic").Trim().ToLowerInvariant();
                if (mode == "exact")
                    return await DocumentKeywordScan.Run(
                        _documentRepository,
                        _fileManager,
                        documentId,
                        query,
                        maxResults,
                        _excerptLinkBuilder
                    );
                if (mode != "semantic")
                    return $"Unknown searchMode \"{searchMode}\" — pass 'semantic' (default) or 'exact'.";
                var chunks = await SearchOrTimeoutFault(
                    () =>
                        _ragManager.SearchRelevantChunksByDocument(
                            query,
                            documentId,
                            maxResults,
                            broadenSparseResults: true
                        ),
                    scoped: true
                );

                if (chunks.Count == 0)
                {
                    // Zero matches on a bad ID must not read as "this filing says nothing
                    // about the topic" — tell the caller the ID itself is wrong.
                    var document = await _documentRepository.Get(documentId);
                    if (document == null)
                        return $"Document {documentId} not found — obtain a valid document ID from ListFilings.";

                    return $"No matching excerpts found in this document ({document.DocumentType} filed {McpFormat.Invariant(document.ReportingDate, "yyyy-MM-dd")}). Try searchMode 'exact' for literal-term matches.";
                }

                var context = await _ragManager.BuildContext(
                    chunks,
                    includeDocumentIds: true,
                    maxExcerptChars: maxExcerptChars,
                    includeExcerptLinks: true
                );
                return context
                    + $"_{chunks.Count} excerpt(s) returned (maxResults {maxResults}); excerpts are in document order — pass an excerpt's line number to ReadDocumentLines for surrounding context._";
            },
            "SearchDocument",
            $"documentId: {documentId}, query: {query}, searchMode: {searchMode}"
        );
    }

    [McpServerTool(Name = "ListFilings", Title = "List Filings", ReadOnly = true)]
    [Description(
        "List stored SEC filings and earnings-call transcripts newest first. Omit ticker for a market-wide feed or provide one ticker for a company-specific list. Returns company identity, document IDs, types, filing and reporting dates, SEC item numbers, line counts, EDGAR accession numbers and acceptance times (UTC; blank for filings stored before October 2026), and page totals. Supports date, document-type, and exact SEC item-number filters. Hidden document types remain excluded unless explicitly requested. Pass a returned ID to SearchDocument or ReadDocumentLines."
    )]
    public Task<string> ListFilings(
        [Description(
            "Optional company ticker symbol (e.g., AAPL, MSFT); a listing outside the US is MIC:TICKER (e.g. XPAR:AIR), whose list includes its ESEF reports. Omit for all companies."
        )]
            string ticker = null,
        [Description("Page number for pagination (default: 1)")] int page = 1,
        [Description("Maximum number of documents per page (default: 10)")] int maxItems = 10,
        [Description("Optional start date filter in YYYY-MM-DD format")] DateTime? startDate = null,
        [Description("Optional end date filter in YYYY-MM-DD format")] DateTime? endDate = null,
        [Description(DocumentTypeDescription)] string documentType = null,
        [Description("Optional exact SEC current-report item number, e.g. 2.02, 5.02, or 1.01.")]
            string itemNumber = null
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
                        return UnknownDocumentType("documentType", documentType);
                }

                var normalizedItemNumber = SecFilingItemNumber.Normalize(itemNumber);
                if (!string.IsNullOrWhiteSpace(itemNumber) && normalizedItemNumber == null)
                    return $"Invalid itemNumber '{itemNumber}'. Use an SEC item number such as 2.02, 5.02, or 1.01.";

                EquityIssuer stock = null;
                string filer = null;
                if (!string.IsNullOrWhiteSpace(ticker))
                {
                    (stock, filer, var filerError) = await ResolveFiler(ticker);
                    if (filerError != null)
                        return filerError;
                }

                maxItems = McpLimit.Clamp(maxItems);
                var offset = ((long)page - 1) * maxItems;
                if (offset > McpLimit.MaxOffset)
                    return $"Page {page} starts beyond the maximum supported offset of {McpFormat.WholeNumber(McpLimit.MaxOffset)} — lower page or narrow the filing filters.";

                int totalCount;
                List<SecDocumentInfo> documents;
                try
                {
                    totalCount = await _secDocumentService.CountDocuments(
                        filer,
                        startDate,
                        endDate,
                        parsedType,
                        normalizedItemNumber
                    );
                    documents = await _secDocumentService.GetRecentDocuments(
                        filer,
                        startDate,
                        endDate,
                        maxItems,
                        page,
                        parsedType,
                        normalizedItemNumber
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
                    var hasFilters =
                        startDate.HasValue
                        || endDate.HasValue
                        || parsedType != null
                        || normalizedItemNumber != null;
                    if (!hasFilters)
                        return stock == null
                            ? "No filings are stored."
                            : $"No documents found for ticker {filer}";

                    var scope = stock == null ? "the market-wide corpus" : filer;
                    var unfiltered = await _secDocumentService.CountDocuments(filer);
                    return $"No documents match the given filters for {scope} — {McpFormat.WholeNumber(unfiltered)} document(s) exist without them. Relax documentType/itemNumber/startDate/endDate.";
                }

                var totalPages = (totalCount + maxItems - 1) / maxItems;
                if (documents.Count == 0)
                    return $"Page {page} is out of range — {McpFormat.WholeNumber(totalCount)} matching document(s) fill only {McpFormat.WholeNumber(totalPages)} page(s) of {maxItems}.";

                var result = MarkdownTable.Start(
                    stock == null
                        ? $"Market-wide filings — page {page} of {McpFormat.WholeNumber(totalPages)} ({McpFormat.WholeNumber(totalCount)} documents):"
                        : $"Financial documents for {MarkdownTable.EscapeCell(stock.Name)} ({MarkdownTable.EscapeCell(filer)}) — page {page} of {McpFormat.WholeNumber(totalPages)} ({McpFormat.WholeNumber(totalCount)} documents):",
                    "Ticker | Company | ID | Type | Filed | Reporting For | Items | Lines | Accession | Accepted (UTC)",
                    "-------|---------|----|------|-------|---------------|-------|-------|-----------|---------------"
                );

                result.AppendRows(
                    documents,
                    doc =>
                    {
                        var items = string.Join(",", SecFilingItemNumber.ParseStored(doc.Items));
                        var accepted = doc.AcceptedAt is { } at
                            ? McpFormat.Invariant(at, "yyyy-MM-dd HH:mm:ss")
                            : "—";
                        var period = doc.ReportingForDate is { } date
                            ? McpFormat.Invariant(date, "yyyy-MM-dd")
                            : "—";
                        return $"{MarkdownTable.EscapeCell(doc.Ticker)} | {MarkdownTable.EscapeCell(doc.CompanyName)} | {doc.Id} | {doc.DocumentType} | {McpFormat.Invariant(doc.ReportingDate, "yyyy-MM-dd")} | {period} | {MarkdownTable.EscapeCell(items, "—")} | {McpFormat.WholeNumber(doc.LineCount)} | {MarkdownTable.EscapeCell(doc.AccessionNumber, "—")} | {accepted}";
                    }
                );

                return result.ToString();
            },
            "ListFilings",
            $"ticker: {ticker}, itemNumber: {itemNumber}"
        );
    }

    private static DocumentType ParseDocumentType(string documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            return null;

        return DocumentType.FromDisplayName(documentType) ?? DocumentType.FromValue(documentType);
    }

    // An unrecognized entry rejects the call with the full accepted list instead of silently
    // searching unfiltered: a near-miss
    // like '10K' or 'Transcript' would otherwise return results the caller believes are
    // filtered, and the mistake is invisible in the output. The accepted list is built from
    // DocumentType.GetAll() at call time, so types registered at host startup (e.g.
    // 'EarningsCallTranscript') are always listed.
    private static bool TryParseDocumentTypes(
        IReadOnlyCollection<string> documentTypes,
        out IReadOnlyCollection<DocumentType> parsed,
        out string error
    )
    {
        parsed = null;
        error = null;
        if (documentTypes == null || documentTypes.Count == 0)
            return true;

        var result = new List<DocumentType>();
        foreach (var entry in documentTypes)
        {
            var type = ParseDocumentType(entry);
            if (type == null)
            {
                error = UnknownDocumentType("documentTypes", entry);
                return false;
            }
            result.Add(type);
        }

        parsed = result.Count > 0 ? result.Distinct().ToList() : null;
        return true;
    }

    private static string UnknownDocumentType(string parameterName, string value) =>
        McpOutput.InvalidArgument(parameterName, value, AcceptedDocumentTypes());

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

    // A bare ticker is the US filer, as before. MIC:TICKER, or a provider suffix after a US miss, names
    // a listing outside the US, scoped to that issuer's documents under the listing's display symbol.
    private async Task<(EquityIssuer Stock, string Filer, string Error)> ResolveFiler(string ticker)
    {
        if (EquityListingSymbol.TryParse(ticker, out _, out _))
        {
            var (venueStock, venue, venueError) = await _commonStockRepository.ResolveListing(
                ticker
            );
            return venue == null
                ? (null, null, venueError)
                : (venueStock, EquityListingSymbol.Display(venue), null);
        }

        var normalizedTicker = McpToolExecutor.NormalizeTicker(ticker);
        if (normalizedTicker == null)
            return (null, null, McpToolExecutor.StockNotFound(ticker));
        EquityIssuer stock = await _commonStockRepository.GetUsByTicker(normalizedTicker);
        if (stock != null)
            return (stock, stock.Presentation.Listing.Ticker, null);

        var notFound = McpToolExecutor.StockNotFound(ticker);
        var (suffixStock, suffixVenue, _) = await _commonStockRepository.ResolveSuffixAfterUsMiss(
            ticker,
            notFound
        );
        return suffixVenue == null
            ? (null, null, notFound)
            : (suffixStock, EquityListingSymbol.Display(suffixVenue), null);
    }

    private static bool TryParseTickers(
        IReadOnlyCollection<string> tickers,
        out IReadOnlyCollection<string> parsed,
        out string error
    )
    {
        parsed = null;
        error = null;
        if (tickers == null || tickers.Count == 0)
            return true;

        var segments = tickers.Select(ticker => ticker?.Trim()).ToList();
        if (segments.Count > MaxExcludedTickers)
        {
            error = $"Maximum {MaxExcludedTickers} excluded tickers per request.";
            return false;
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            var ticker = TickerNormalizer.NormalizeDashListed(segment);
            if (ticker == null)
            {
                error =
                    $"Invalid excluded ticker '{segment}'. Use 1-32 ASCII letters, digits, dots, or dashes.";
                return false;
            }
            if (seen.Add(ticker))
                normalized.Add(ticker);
        }

        parsed = normalized;
        return true;
    }

    // A contradictory window (start after end) must error instead of returning the generic
    // empty-result message — the caller would conclude no such documents exist.
    private static string ValidateDateRange(DateTime? startDate, DateTime? endDate) =>
        startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value
            ? $"startDate {McpFormat.Invariant(startDate.Value, "yyyy-MM-dd")} is after endDate {McpFormat.Invariant(endDate.Value, "yyyy-MM-dd")} — swap the values."
            : null;

    // Turns a BM25 statement-budget timeout into a fault the caller can act on, keeping the
    // original exception attached so the recorded Errors row still shows what actually failed.
    // Every other failure keeps the executor's catch-all wording.
    internal static async Task<List<Chunk>> SearchOrTimeoutFault(
        Func<Task<List<Chunk>>> search,
        bool scoped,
        SearchMatch match = SearchMatch.Ranked
    )
    {
        try
        {
            return await search();
        }
        catch (ChunkSearchTimeoutException exception)
        {
            throw new McpToolFaultException(SearchTimeoutAdvice.For(scoped, match), exception);
        }
    }

    internal static bool TryParseSearchMatch(string value, out SearchMatch match)
    {
        switch ((value ?? "ranked").Trim().ToLowerInvariant())
        {
            case "ranked":
                match = SearchMatch.Ranked;
                return true;
            case "phrase":
                match = SearchMatch.Phrase;
                return true;
            default:
                match = SearchMatch.Ranked;
                return false;
        }
    }

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
