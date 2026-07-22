using System.ComponentModel;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class DocumentTextTools
{
    private readonly DocumentRepository _documentRepository;
    private readonly IFileManager _fileManager;
    private readonly McpToolRunner _runner;

    public DocumentTextTools(
        DocumentRepository documentRepository,
        ErrorManager errorManager,
        IFileManager fileManager,
        ILogger<DocumentTextTools> logger
    )
    {
        _documentRepository = documentRepository;
        _fileManager = fileManager;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(
        Name = "SearchDocumentKeyword",
        Title = "Keyword Search Within a Filing",
        ReadOnly = true
    )]
    [Description(
        "Perform a case-insensitive keyword search within a specific SEC filing or earnings call transcript by document ID. Returns matching lines with surrounding context and line numbers, making it ideal for finding exact terms, figures, or phrases that semantic search might miss. Typographic punctuation is folded before matching, so a plain-ASCII keyword (e.g. \"world's\") matches the smart punctuation stored in filings. The header reports the total number of matching lines even when only the first ones are shown. Use this after ListCompanyDocuments to locate precise occurrences of a keyword (e.g., a revenue figure, risk factor term, or executive name) within a known document. Complements semantic search tools by providing exact text matches rather than meaning-based results. Use ReadDocumentLines to read broader sections around matches."
    )]
    public Task<string> SearchDocumentKeyword(
        [Description("Document ID obtained from ListCompanyDocuments")] Guid documentId,
        [Description("Keyword or phrase to search for (case-insensitive)")] string keyword,
        [Description("Maximum number of matching lines to return (default: 20, max: 500)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            () =>
                DocumentKeywordScan.Run(
                    _documentRepository,
                    _fileManager,
                    documentId,
                    keyword,
                    maxResults
                ),
            "SearchDocumentKeyword",
            $"documentId: {documentId}, keyword: {keyword}",
            "An error occurred while searching the document. Please try again."
        );
    }

    // Ceiling on the number of lines a single call returns: prod documents reach 500k+
    // lines, and an uncapped range request would return megabytes in one MCP response,
    // blowing the consumer's context window. The truncation note makes continuation
    // self-describing.
    private const int MaxLinesPerRead = 2000;

    [McpServerTool(Name = "ReadDocumentLines", Title = "Read Filing Lines", ReadOnly = true)]
    [Description(
        "Read a specific range of lines from an SEC filing or earnings call transcript by document ID. Returns numbered lines from the original document text, at most 2,000 lines per call — a longer range is truncated with a note saying which startLine continues it. Use this to read sections of a filing that were identified by SearchDocumentKeyword (by line number) or by semantic search tools (by approximate line number shown in excerpts). Ideal for reading full tables, paragraphs, or sections that may have been truncated in search results. The document ID and line range must be known beforehand — use ListCompanyDocuments to find documents and SearchDocumentKeyword or semantic search to identify relevant line numbers."
    )]
    public Task<string> ReadDocumentLines(
        [Description("Document ID obtained from ListCompanyDocuments")] Guid documentId,
        [Description("First line to read (1-based, inclusive)")] int startLine,
        [Description(
            "Last line to read (1-based, inclusive). At most 2,000 lines are returned per call; a longer range is truncated with a note on how to continue."
        )]
            int endLine
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (document, lines, error) = await DocumentKeywordScan.LoadDocumentLines(
                    _documentRepository,
                    _fileManager,
                    documentId
                );
                if (error != null)
                    return error;

                var totalLines = lines.Length;

                // Validate against the ORIGINAL arguments before any clamping: an error
                // message quoting a clamped value the caller never sent reads as if the
                // tool misparsed the request.
                if (endLine < startLine)
                {
                    return $"Invalid line range: {startLine} to {endLine} — startLine is after endLine.";
                }

                if (startLine > totalLines)
                {
                    return $"startLine {McpFormat.WholeNumber(startLine)} is beyond the end of the document ({McpFormat.WholeNumber(totalLines)} lines).";
                }

                startLine = Math.Max(1, startLine);
                endLine = Math.Min(totalLines, endLine);

                if (endLine < startLine)
                {
                    return $"Invalid line range: {startLine} to {endLine} (document has {McpFormat.WholeNumber(totalLines)} lines).";
                }

                var truncated = endLine - startLine + 1 > MaxLinesPerRead;
                if (truncated)
                {
                    endLine = startLine + MaxLinesPerRead - 1;
                }

                var result = new StringBuilder();
                result.AppendLine(
                    $"{DocumentTextFormat.Header(document)} — lines {McpFormat.WholeNumber(startLine)} to {McpFormat.WholeNumber(endLine)} of {McpFormat.WholeNumber(totalLines)}:"
                );
                result.AppendLine();

                for (var i = startLine - 1; i < endLine; i++)
                {
                    result.AppendLine(DocumentTextFormat.Line(i + 1, lines[i]));
                }

                if (truncated)
                {
                    result.AppendLine();
                    result.AppendLine(
                        $"_Returned the first {McpFormat.WholeNumber(MaxLinesPerRead)} lines of the requested range — continue with startLine={McpFormat.WholeNumber(endLine + 1)}._"
                    );
                }

                return result.ToString();
            },
            "ReadDocumentLines",
            $"documentId: {documentId}, lines: {startLine}-{endLine}",
            "An error occurred while reading document lines. Please try again."
        );
    }
}
