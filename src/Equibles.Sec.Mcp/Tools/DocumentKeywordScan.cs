using System.Text;
using Equibles.Mcp.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;

namespace Equibles.Sec.Mcp.Tools;

// The exact-match scan shared by SearchDocumentKeyword and SearchDocument's searchMode="exact":
// loads the document text, matches the keyword as a typography-folded case-insensitive substring
// per line, and renders each match with one line of context, grep-style. One implementation so the
// two tools can never drift on folding, counting, or rendering.
internal static class DocumentKeywordScan
{
    internal static async Task<string> Run(
        DocumentRepository documentRepository,
        IFileManager fileManager,
        Guid documentId,
        string keyword,
        int maxResults,
        IDocumentExcerptLinkBuilder linkBuilder = null
    )
    {
        var (document, lines, error) = await LoadDocumentLines(
            documentRepository,
            fileManager,
            documentId
        );
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
            return $"No matches found for \"{keyword}\" in {DocumentTextFormat.Header(document)}.";
        }

        // Count BEFORE truncating: presenting the capped count as the total makes
        // the caller state "the filing mentions X exactly N times" and stop early.
        var matches = allMatches.Take(maxResults).ToList();

        var result = new StringBuilder();
        result.AppendLine(
            $"Keyword search for \"{keyword}\" in {DocumentTextFormat.Header(document)} — {McpFormat.WholeNumber(allMatches.Count)} matching line(s):"
        );
        result.AppendLine();

        AppendMatchBlocks(result, lines, matches, keyword, document, linkBuilder);

        result.AppendLine(McpOutput.TruncationNote(matches.Count, allMatches.Count));

        return result.ToString();
    }

    internal static async Task<(Document Document, string[] Lines, string Error)> LoadDocumentLines(
        DocumentRepository documentRepository,
        IFileManager fileManager,
        Guid documentId
    )
    {
        var document = await documentRepository.GetWithContent(documentId);
        if (document == null)
            return (null, null, $"Document {documentId} not found.");
        if (document.Content == null)
            return (null, null, $"Document {documentId} has no content.");

        var bytes = await fileManager.GetContent(document.Content);
        if (bytes == null)
            return (null, null, $"Document {documentId} has no content.");

        var text = Encoding.UTF8.GetString(bytes);
        return (document, text.Split('\n'), null);
    }

    // Renders each match with one line of context on each side, merging overlapping and
    // adjacent blocks grep-style so a document line never prints twice; a blank line
    // separates non-contiguous blocks. When a link builder is registered, each block ends
    // with a link anchored on the block's line span and its first matched line's text.
    private static void AppendMatchBlocks(
        StringBuilder result,
        string[] lines,
        List<int> matches,
        string keyword,
        Document document,
        IDocumentExcerptLinkBuilder linkBuilder
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
        int? blockStart = null;
        int? blockFirstMatch = null;
        foreach (var lineIndex in linesToPrint)
        {
            if (previous.HasValue && lineIndex > previous.Value + 1)
            {
                AppendBlockLink(
                    result,
                    lines,
                    document,
                    linkBuilder,
                    blockStart,
                    previous,
                    blockFirstMatch
                );
                result.AppendLine();
                blockStart = null;
                blockFirstMatch = null;
            }

            blockStart ??= lineIndex;
            var isMatch = matchSet.Contains(lineIndex);
            if (isMatch && blockFirstMatch == null)
                blockFirstMatch = lineIndex;

            var content = isMatch ? HighlightKeyword(lines[lineIndex], keyword) : lines[lineIndex];
            result.AppendLine(DocumentTextFormat.Line(lineIndex + 1, content));
            previous = lineIndex;
        }

        AppendBlockLink(
            result,
            lines,
            document,
            linkBuilder,
            blockStart,
            previous,
            blockFirstMatch
        );
        result.AppendLine();
    }

    private static void AppendBlockLink(
        StringBuilder result,
        string[] lines,
        Document document,
        IDocumentExcerptLinkBuilder linkBuilder,
        int? blockStart,
        int? blockEnd,
        int? blockFirstMatch
    )
    {
        if (
            linkBuilder == null
            || blockStart == null
            || blockEnd == null
            || blockFirstMatch == null
        )
            return;

        // Line numbers are rendered 1-based; the builder gets the same basis.
        var url = linkBuilder.BuildExcerptUrl(
            document,
            blockStart.Value + 1,
            blockEnd.Value + 1,
            lines[blockFirstMatch.Value]
        );
        if (url != null)
            result.AppendLine($"Link: {url}");
    }

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
