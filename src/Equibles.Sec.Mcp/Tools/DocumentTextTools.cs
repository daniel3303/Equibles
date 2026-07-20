using System.ComponentModel;
using System.Text;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
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
            async () =>
            {
                var (document, lines, error) = await LoadDocumentLines(documentId);
                if (error != null)
                    return error;

                maxResults = McpLimit.Clamp(maxResults);

                // Fold typography on BOTH sides: filings store smart punctuation (U+2019
                // apostrophes, curly quotes) while callers type ASCII, so an unfolded
                // ordinal Contains reports false negatives for text the document contains.
                var foldedKeyword = TypographyFold.Fold(keyword);
                var allMatches = Enumerable
                    .Range(0, lines.Length)
                    .Where(i =>
                        TypographyFold
                            .Fold(lines[i])
                            .Contains(foldedKeyword, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList();

                if (allMatches.Count == 0)
                {
                    return $"No matches found for \"{keyword}\" in {FormatDocumentHeader(document)}.";
                }

                // Count BEFORE truncating: presenting the capped count as the total makes
                // the caller state "the filing mentions X exactly N times" and stop early.
                var matches = allMatches.Take(maxResults).ToList();

                var result = new StringBuilder();
                result.AppendLine(
                    $"Keyword search for \"{keyword}\" in {FormatDocumentHeader(document)} — {McpFormat.WholeNumber(allMatches.Count)} matching line(s):"
                );
                result.AppendLine();

                AppendMatchBlocks(result, lines, matches, keyword);

                result.AppendLine(McpOutput.TruncationNote(matches.Count, allMatches.Count));

                return result.ToString();
            },
            "SearchDocumentKeyword",
            $"documentId: {documentId}, keyword: {keyword}",
            "An error occurred while searching the document. Please try again."
        );
    }

    // Renders each match with one line of context on each side, merging overlapping and
    // adjacent blocks grep-style so a document line never prints twice; a blank line
    // separates non-contiguous blocks.
    private static void AppendMatchBlocks(
        StringBuilder result,
        string[] lines,
        List<int> matches,
        string keyword
    )
    {
        var matchSet = matches.ToHashSet();
        var linesToPrint = new SortedSet<int>();
        foreach (var lineIndex in matches)
        {
            if (lineIndex > 0)
                linesToPrint.Add(lineIndex - 1);
            linesToPrint.Add(lineIndex);
            if (lineIndex < lines.Length - 1)
                linesToPrint.Add(lineIndex + 1);
        }

        int? previous = null;
        foreach (var lineIndex in linesToPrint)
        {
            if (previous.HasValue && lineIndex > previous.Value + 1)
                result.AppendLine();

            var content = matchSet.Contains(lineIndex)
                ? HighlightKeyword(lines[lineIndex], keyword)
                : lines[lineIndex];
            result.AppendLine(FormatLine(lineIndex + 1, content));
            previous = lineIndex;
        }

        result.AppendLine();
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
                var (document, lines, error) = await LoadDocumentLines(documentId);
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
                    $"{FormatDocumentHeader(document)} — lines {McpFormat.WholeNumber(startLine)} to {McpFormat.WholeNumber(endLine)} of {McpFormat.WholeNumber(totalLines)}:"
                );
                result.AppendLine();

                for (var i = startLine - 1; i < endLine; i++)
                {
                    result.AppendLine(FormatLine(i + 1, lines[i]));
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

    private async Task<(Document Document, string[] Lines, string Error)> LoadDocumentLines(
        Guid documentId
    )
    {
        var document = await _documentRepository.GetWithContent(documentId);
        if (document == null)
            return (null, null, $"Document {documentId} not found.");
        if (document.Content == null)
            return (null, null, $"Document {documentId} has no content.");

        var bytes = await _fileManager.GetContent(document.Content);
        if (bytes == null)
            return (null, null, $"Document {documentId} has no content.");

        var text = Encoding.UTF8.GetString(bytes);
        return (document, text.Split('\n'), null);
    }

    private static string FormatLine(int lineNumber, string content)
    {
        return $"{lineNumber, 6} │ {content}";
    }

    private static string FormatDocumentHeader(Document document) =>
        $"{document.CommonStock.Name} ({document.CommonStock.Ticker}) {document.DocumentType} filed {McpFormat.Invariant(document.ReportingDate, "yyyy-MM-dd")}";

    private static string HighlightKeyword(string line, string keyword)
    {
        // An empty keyword makes IndexOf return the start position every
        // iteration, so index never advances and the loop runs unbounded
        // (DoS — keyword reaches here unvalidated from the MCP tool).
        if (string.IsNullOrEmpty(keyword))
            return line;

        // Match on the folded pair so typography-folded matches still bold; the fold is
        // one-to-one per char, so folded indices address the same span in the original
        // line, which is what gets emitted.
        var foldedLine = TypographyFold.Fold(line);
        var foldedKeyword = TypographyFold.Fold(keyword);

        var result = new StringBuilder();
        var index = 0;

        while (index < line.Length)
        {
            var matchIndex = foldedLine.IndexOf(
                foldedKeyword,
                index,
                StringComparison.OrdinalIgnoreCase
            );
            if (matchIndex < 0)
            {
                result.Append(line, index, line.Length - index);
                break;
            }

            result.Append(line, index, matchIndex - index);
            result.Append("**");
            result.Append(line, matchIndex, foldedKeyword.Length);
            result.Append("**");
            index = matchIndex + foldedKeyword.Length;
        }

        return result.ToString();
    }
}
