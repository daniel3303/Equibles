using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Equibles.Core.AutoWiring;
using Equibles.Sec.BusinessLogic.Tokenization;
using Microsoft.ML.Tokenizers;

namespace Equibles.Sec.BusinessLogic.Processing;

[Service]
public class ChunkingStrategy
{
    private const int ChunkTokenSize = 1024;
    private const int OverlapTokenSize = 128;

    private readonly TiktokenTokenizer _tokenizer;

    public ChunkingStrategy(TokenCounter tokenCounter)
    {
        _tokenizer = tokenCounter.Tokenizer;
    }

    public List<ChunkInfo> SplitIntoChunks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // One encode places every token in the text, so a boundary is a lookup. Decoding the
        // tokens before each boundary instead made a filing's cost grow with the square of its
        // length, and copied the text read so far for every chunk.
        var tokens = _tokenizer.EncodeToTokens(text, out _);
        var chunks = new List<ChunkInfo>();
        var tokenOffset = 0;
        var startLineNumber = 1;
        var linesCountedTo = 0;

        while (tokenOffset < tokens.Count)
        {
            var windowEnd = Math.Min(tokenOffset + ChunkTokenSize, tokens.Count);
            var startPosition = FindCharPosition(text, tokens, tokenOffset);
            var endPosition = FindCharPosition(text, tokens, windowEnd);
            var chunkText = text.Substring(startPosition, endPosition - startPosition);

            // Try to break at a sentence boundary within the overlap zone
            if (windowEnd < tokens.Count)
            {
                var overlapStart = Math.Max(0, windowEnd - OverlapTokenSize);
                var overlapStartChar = FindCharPosition(text, tokens, overlapStart);
                var bestBreak = FindLastSentenceEnd(
                    chunkText,
                    overlapStartChar - startPosition,
                    chunkText.Length
                );

                if (bestBreak > 0 && bestBreak < chunkText.Length)
                {
                    chunkText = chunkText.Substring(0, bestBreak);
                    endPosition = startPosition + bestBreak;
                }
            }

            startLineNumber += text.AsSpan(linesCountedTo, startPosition - linesCountedTo)
                .Count('\n');
            linesCountedTo = startPosition;

            chunks.Add(
                new ChunkInfo(startPosition, endPosition, startLineNumber, CleanText(chunkText))
            );

            // Advance by chunk size minus overlap
            var advance = ChunkTokenSize - OverlapTokenSize;
            if (tokenOffset + advance <= tokenOffset)
                advance = 1;
            tokenOffset += advance;
        }

        return chunks;
    }

    public string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Strip any residual HTML tags using AngleSharp
        var parser = new HtmlParser();
        using var document = parser.ParseDocument($"<body>{text}</body>");

        // TextContent concatenates the text of ALL descendants — including
        // raw-text elements (<script>/<style>) and comments. SEC EDGAR HTML
        // routinely carries boilerplate inline JS/CSS; left in, its source
        // pollutes embeddings and document search. Remove those nodes first.
        foreach (var node in document.QuerySelectorAll("script, style").ToList())
        {
            node.Remove();
        }
        foreach (var comment in document.Body.Descendants<IComment>().ToList())
        {
            comment.Remove();
        }

        // TextContent concatenates descendant text with NO separator, so adjacent
        // block-level cells (a table label and its figure, or sibling <p>/<div>/<li>)
        // glue into one junk token ("Net income1000") that never matches a search
        // query. Join distinct text nodes with a space, but preserve line breaks already
        // present in Markdown so filing-table rows remain independently readable.
        text = string.Join(" ", document.Body.Descendants<IText>().Select(node => node.Data));

        text = Regex.Replace(text, @"[^\S\r\n]+", " ");
        text = Regex.Replace(text, @" *\r?\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    // Where a token starts in the text. The tokenizer never starts one inside a character, so a
    // chunk cut here holds whole characters only.
    private static int FindCharPosition(
        string text,
        IReadOnlyList<EncodedToken> tokens,
        int tokenIndex
    ) => tokenIndex >= tokens.Count ? text.Length : tokens[tokenIndex].Offset.Start.Value;

    private int FindLastSentenceEnd(string text, int start, int end)
    {
        if (start < 0)
            start = 0;
        var searchText = text.Substring(start, end - start);
        var sentenceEndings = new[] { ".", "!", "?", "\n\n" };

        var lastIndex = sentenceEndings.Max(ending =>
            searchText.LastIndexOf(ending, StringComparison.Ordinal)
        );

        return lastIndex > -1 ? start + lastIndex + 1 : -1;
    }
}
