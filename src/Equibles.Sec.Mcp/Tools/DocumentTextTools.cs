using System.ComponentModel;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Core.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class DocumentTextTools {
    private readonly DocumentRepository _documentRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<DocumentTextTools> _logger;

    public DocumentTextTools(DocumentRepository documentRepository, ErrorManager errorManager,
        ILogger<DocumentTextTools> logger) {
        _documentRepository = documentRepository;
        _errorManager = errorManager;
        _logger = logger;
    }

    [McpServerTool(Name = "SearchDocumentKeyword")]
    [Description("Perform a case-insensitive keyword search within a specific SEC filing or earnings call transcript by document ID. Returns matching lines with surrounding context and line numbers, making it ideal for finding exact terms, figures, or phrases that semantic search might miss. Use this after ListCompanyDocuments to locate precise occurrences of a keyword (e.g., a revenue figure, risk factor term, or executive name) within a known document. Complements semantic search tools by providing exact text matches rather than meaning-based results. Use ReadDocumentLines to read broader sections around matches.")]
    public async Task<string> SearchDocumentKeyword(
        [Description("Document ID obtained from ListCompanyDocuments")] Guid documentId,
        [Description("Keyword or phrase to search for (case-insensitive)")] string keyword,
        [Description("Maximum number of matches to return (default: 20)")] int maxResults = 20
    ) {
        try {
            var document = await _documentRepository.GetWithContent(documentId);
            if (document == null) return $"Document {documentId} not found.";
            if (document.Content?.FileContent?.Bytes == null) return $"Document {documentId} has no content.";

            var text = Encoding.UTF8.GetString(document.Content.FileContent.Bytes);
            var lines = text.Split('\n');
            var matches = new List<int>();

            for (var i = 0; i < lines.Length && matches.Count < maxResults; i++) {
                if (lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
                    matches.Add(i);
                }
            }

            if (matches.Count == 0) {
                return $"No matches found for \"{keyword}\" in {document.CommonStock.Name} ({document.CommonStock.Ticker}) {document.DocumentType} filed {document.ReportingDate:yyyy-MM-dd}.";
            }

            var result = new StringBuilder();
            result.AppendLine($"Keyword search for \"{keyword}\" in {document.CommonStock.Name} ({document.CommonStock.Ticker}) {document.DocumentType} filed {document.ReportingDate:yyyy-MM-dd} — {matches.Count} matches found:");
            result.AppendLine();

            foreach (var lineIndex in matches) {
                var lineNumber = lineIndex + 1;

                // Line before
                if (lineIndex > 0) {
                    result.AppendLine(FormatLine(lineIndex, lines[lineIndex - 1]));
                }

                // Matched line with bold markers
                var highlightedLine = HighlightKeyword(lines[lineIndex], keyword);
                result.AppendLine(FormatLine(lineNumber, highlightedLine));

                // Line after
                if (lineIndex < lines.Length - 1) {
                    result.AppendLine(FormatLine(lineNumber + 1, lines[lineIndex + 1]));
                }

                result.AppendLine();
            }

            return result.ToString();
        } catch (Exception ex) {
            _logger.LogError(ex, "SearchDocumentKeyword failed for document {DocumentId}", documentId);
            try { await _errorManager.Create(ErrorSource.McpTool, "SearchDocumentKeyword", ex.Message, ex.StackTrace, $"documentId: {documentId}, keyword: {keyword}"); } catch { }
            return "An error occurred while searching the document. Please try again.";
        }
    }

    [McpServerTool(Name = "ReadDocumentLines")]
    [Description("Read a specific range of lines from an SEC filing or earnings call transcript by document ID. Returns numbered lines from the original document text. Use this to read sections of a filing that were identified by SearchDocumentKeyword (by line number) or by semantic search tools (by approximate line number shown in excerpts). Ideal for reading full tables, paragraphs, or sections that may have been truncated in search results. The document ID and line range must be known beforehand — use ListCompanyDocuments to find documents and SearchDocumentKeyword or semantic search to identify relevant line numbers.")]
    public async Task<string> ReadDocumentLines(
        [Description("Document ID obtained from ListCompanyDocuments")] Guid documentId,
        [Description("First line to read (1-based, inclusive)")] int startLine,
        [Description("Last line to read (1-based, inclusive)")] int endLine
    ) {
        try {
            var document = await _documentRepository.GetWithContent(documentId);
            if (document == null) return $"Document {documentId} not found.";
            if (document.Content?.FileContent?.Bytes == null) return $"Document {documentId} has no content.";

            var text = Encoding.UTF8.GetString(document.Content.FileContent.Bytes);
            var lines = text.Split('\n');
            var totalLines = lines.Length;

            // Clamp to valid range
            startLine = Math.Max(1, startLine);
            endLine = Math.Min(totalLines, endLine);

            if (startLine > endLine) {
                return $"Invalid line range: {startLine} to {endLine} (document has {totalLines:N0} lines).";
            }

            var result = new StringBuilder();
            result.AppendLine($"{document.CommonStock.Name} ({document.CommonStock.Ticker}) {document.DocumentType} filed {document.ReportingDate:yyyy-MM-dd} — lines {startLine:N0} to {endLine:N0} of {totalLines:N0}:");
            result.AppendLine();

            for (var i = startLine - 1; i < endLine; i++) {
                result.AppendLine(FormatLine(i + 1, lines[i]));
            }

            return result.ToString();
        } catch (Exception ex) {
            _logger.LogError(ex, "ReadDocumentLines failed for document {DocumentId}", documentId);
            try { await _errorManager.Create(ErrorSource.McpTool, "ReadDocumentLines", ex.Message, ex.StackTrace, $"documentId: {documentId}, lines: {startLine}-{endLine}"); } catch { }
            return "An error occurred while reading document lines. Please try again.";
        }
    }

    private static string FormatLine(int lineNumber, string content) {
        return $"{lineNumber,6} │ {content}";
    }

    private static string HighlightKeyword(string line, string keyword) {
        var result = new StringBuilder();
        var index = 0;

        while (index < line.Length) {
            var matchIndex = line.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0) {
                result.Append(line, index, line.Length - index);
                break;
            }

            result.Append(line, index, matchIndex - index);
            result.Append("**");
            result.Append(line, matchIndex, keyword.Length);
            result.Append("**");
            index = matchIndex + keyword.Length;
        }

        return result.ToString();
    }
}
