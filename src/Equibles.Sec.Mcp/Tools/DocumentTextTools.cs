using System.ComponentModel;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class DocumentTextTools
{
    private readonly DocumentRepository _documentRepository;
    private readonly IFileManager _fileManager;
    private readonly IDocumentExcerptLinkBuilder _excerptLinkBuilder;
    private readonly McpToolRunner _runner;

    public DocumentTextTools(
        DocumentRepository documentRepository,
        ErrorManager errorManager,
        IFileManager fileManager,
        ILogger<DocumentTextTools> logger,
        IDocumentExcerptLinkBuilder excerptLinkBuilder = null
    )
    {
        _documentRepository = documentRepository;
        _fileManager = fileManager;
        _excerptLinkBuilder = excerptLinkBuilder;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    // Kept as a non-tool seam for exact-search tests and callers migrating to
    // SearchDocument(searchMode: "exact").
    public Task<string> SearchDocumentKeyword(
        Guid documentId,
        string keyword,
        int maxResults = 20
    ) =>
        _runner.Execute(
            () =>
                DocumentKeywordScan.Run(
                    _documentRepository,
                    _fileManager,
                    documentId,
                    keyword,
                    maxResults,
                    _excerptLinkBuilder
                ),
            "SearchDocument",
            $"documentId: {documentId}, query: {keyword}, searchMode: exact",
            "An error occurred while searching the document. Please try again."
        );

    // Ceiling on the number of lines a single call returns: prod documents reach 500k+
    // lines, and an uncapped range request would return megabytes in one MCP response,
    // blowing the consumer's context window. The truncation note makes continuation
    // self-describing.
    private const int MaxLinesPerRead = 2000;

    [McpServerTool(Name = "ReadDocumentLines", Title = "Read Filing Lines", ReadOnly = true)]
    [Description(
        "Read numbered lines from one SEC filing or earnings-call transcript. Use line numbers returned by SearchDocument or request a known range. Returns at most 2,000 lines and identifies the next startLine when truncated."
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
