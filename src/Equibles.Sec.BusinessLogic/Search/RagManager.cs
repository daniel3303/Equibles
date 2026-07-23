using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.BusinessLogic.Search;

[Service(ServiceLifetime.Scoped, typeof(IRagManager))]
public class RagManager : IRagManager
{
    private readonly HybridChunkSearcher _hybridChunkSearcher;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ILogger<RagManager> _logger;

    public RagManager(
        HybridChunkSearcher hybridChunkSearcher,
        CommonStockRepository commonStockRepository,
        ILogger<RagManager> logger
    )
    {
        _hybridChunkSearcher = hybridChunkSearcher;
        _commonStockRepository = commonStockRepository;
        _logger = logger;
    }

    public async Task<List<Chunk>> SearchRelevantChunks(
        string query,
        int maxResults = 5,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        IReadOnlyCollection<string> excludeTickers = null,
        int maxResultsPerCompany = 0,
        bool broadenSparseResults = false
    )
    {
        var chunks = await _hybridChunkSearcher.Search(
            query,
            maxResults,
            excludeTickers: excludeTickers,
            documentTypes: documentTypes,
            maxResultsPerCompany: maxResultsPerCompany,
            startDate: startDate,
            endDate: endDate,
            disjunctiveFallback: broadenSparseResults
        );

        _logger.LogInformation("Found {Count} relevant chunks for query", chunks.Count);
        return chunks;
    }

    public async Task<List<Chunk>> SearchRelevantChunksByCompany(
        string query,
        string ticker,
        int maxResults = 5,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool broadenSparseResults = false
    )
    {
        ticker = await ResolvePrimaryTicker(ticker);
        var chunks = await _hybridChunkSearcher.Search(
            query,
            maxResults,
            ticker,
            documentTypes: documentTypes,
            startDate: startDate,
            endDate: endDate,
            disjunctiveFallback: broadenSparseResults
        );

        _logger.LogInformation(
            "Found {Count} relevant chunks for company {Ticker}",
            chunks.Count,
            ticker
        );
        return chunks;
    }

    public async Task<List<Chunk>> SearchRelevantChunksByDocument(
        string query,
        Guid documentId,
        int maxResults = 5,
        bool broadenSparseResults = false
    )
    {
        var chunks = await _hybridChunkSearcher.Search(
            query,
            maxResults,
            documentId: documentId,
            disjunctiveFallback: broadenSparseResults
        );

        _logger.LogInformation(
            "Found {Count} relevant chunks for document {DocumentId}",
            chunks.Count,
            documentId
        );
        return chunks;
    }

    public async Task<List<Chunk>> SearchRelevantChunksByDocumentType(
        string query,
        DocumentType documentType,
        int maxResults = 5
    )
    {
        return await SearchRelevantChunks(query, maxResults, [documentType]);
    }

    public Task<string> BuildContext(
        List<Chunk> chunks,
        bool includeDocumentIds = false,
        int maxExcerptChars = 0
    )
    {
        if (!chunks.Any())
        {
            return Task.FromResult("No relevant financial documents found.");
        }

        var context = new StringBuilder();
        context.AppendLine("Relevant financial document excerpts:");
        context.AppendLine();

        // Grouping keys on the document ID: two distinct filings of the same type filed the
        // same day (e.g. two 8-Ks) must render as two documents, especially when the header
        // carries the ID the caller will drill into.
        var groupedChunks = chunks.GroupBy(c => new
        {
            c.Document.Id,
            c.Document.CommonStock.Ticker,
            c.Document.DocumentType,
            c.Document.ReportingDate,
        });

        foreach (var group in groupedChunks)
        {
            var firstChunk = group.First();
            context.AppendLine($"## {firstChunk.Document.CommonStock.Name} ({group.Key.Ticker})");
            var filedOn = group.Key.ReportingDate.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
            context.AppendLine(
                includeDocumentIds
                    ? $"**Document:** {group.Key.DocumentType} filed on {filedOn} (ID: {group.Key.Id})"
                    : $"**Document:** {group.Key.DocumentType} filed on {filedOn}"
            );
            context.AppendLine();

            foreach (var chunk in group.OrderBy(c => c.StartPosition))
            {
                var content = RenderExcerptContent(chunk.Content, maxExcerptChars);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    context.AppendLine(
                        chunk.StartLineNumber > 0
                            ? $"**Excerpt {chunk.Index + 1} (line ~{chunk.StartLineNumber.ToString("N0", CultureInfo.InvariantCulture)}):**"
                            : $"**Excerpt {chunk.Index + 1}:**"
                    );
                    context.AppendLine(content);
                    context.AppendLine();
                }
            }

            context.AppendLine("---");
            context.AppendLine();
        }

        return Task.FromResult(context.ToString());
    }

    // Markdown image references in chunk content (slide-deck captures embed one per slide)
    // add tokens without information for a text consumer — the file name is meaningless
    // outside the original filing. Stripped at render time only; the stored chunk content
    // is untouched.
    private static readonly Regex ImageMarkdown = new(
        @"!\[[^\]]*\]\([^)]*\)",
        RegexOptions.Compiled
    );

    private static string RenderExcerptContent(string content, int maxExcerptChars)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        content = ImageMarkdown.Replace(content, string.Empty).Trim();

        if (maxExcerptChars <= 0 || content.Length <= maxExcerptChars)
        {
            return content;
        }

        // Cut at the last whitespace inside the budget so the excerpt never ends mid-word,
        // and say the cut happened — a silently shortened excerpt would read as the chunk's
        // full text.
        var cut = content.LastIndexOf(' ', maxExcerptChars - 1);
        if (cut <= 0)
        {
            cut = maxExcerptChars;
        }

        return content[..cut].TrimEnd()
            + " […truncated — raise maxExcerptChars or use ReadDocumentLines for the full text]";
    }

    private async Task<string> ResolvePrimaryTicker(string ticker)
    {
        var stock = await _commonStockRepository.GetByTicker(ticker);
        return stock?.Ticker ?? ticker;
    }
}
