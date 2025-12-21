using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Tokenization;
using Equibles.Core.AutoWiring;
using Microsoft.ML.Tokenizers;

namespace Equibles.Sec.BusinessLogic.Processing;

[Service]
public class ChunkingStrategy {
    private const int ChunkTokenSize = 1024;
    private const int OverlapTokenSize = 128;

    private readonly TiktokenTokenizer _tokenizer;

    public ChunkingStrategy(TokenCounter tokenCounter) {
        _tokenizer = tokenCounter.Tokenizer;
    }

    public List<ChunkInfo> SplitIntoChunks(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return [];
        }

        var tokenIds = _tokenizer.EncodeToIds(text);
        var chunks = new List<ChunkInfo>();
        var tokenOffset = 0;

        while (tokenOffset < tokenIds.Count) {
            var windowEnd = Math.Min(tokenOffset + ChunkTokenSize, tokenIds.Count);
            var windowIds = tokenIds.Skip(tokenOffset).Take(windowEnd - tokenOffset).ToArray();
            var chunkText = _tokenizer.Decode(windowIds);

            // Find character positions in the original text
            var startPosition = FindCharPosition(text, tokenIds, tokenOffset);
            var endPosition = FindCharPosition(text, tokenIds, windowEnd);

            // Try to break at a sentence boundary within the overlap zone
            if (windowEnd < tokenIds.Count) {
                var overlapStart = Math.Max(0, windowEnd - OverlapTokenSize);
                var overlapStartChar = FindCharPosition(text, tokenIds, overlapStart);
                var bestBreak = FindLastSentenceEnd(chunkText, overlapStartChar - startPosition, chunkText.Length);

                if (bestBreak > 0 && bestBreak < chunkText.Length) {
                    chunkText = chunkText.Substring(0, bestBreak);
                    endPosition = startPosition + bestBreak;
                }
            }

            var startLineNumber = text.Substring(0, startPosition).Count(c => c == '\n') + 1;

            chunks.Add(new ChunkInfo(startPosition, endPosition, startLineNumber, CleanText(chunkText)));

            // Advance by chunk size minus overlap
            var advance = ChunkTokenSize - OverlapTokenSize;
            if (tokenOffset + advance <= tokenOffset) advance = 1;
            tokenOffset += advance;
        }

        return chunks;
    }

    public string CleanText(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }

        // Strip any residual HTML tags using AngleSharp
        var parser = new HtmlParser();
        using var document = parser.ParseDocument($"<body>{text}</body>");
        text = document.Body.TextContent;

        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private int FindCharPosition(string text, IReadOnlyList<int> allTokenIds, int tokenIndex) {
        if (tokenIndex >= allTokenIds.Count) return text.Length;
        if (tokenIndex <= 0) return 0;

        var precedingIds = allTokenIds.Take(tokenIndex).ToArray();
        var decoded = _tokenizer.Decode(precedingIds);
        return Math.Min(decoded.Length, text.Length);
    }

    private int FindLastSentenceEnd(string text, int start, int end) {
        if (start < 0) start = 0;
        var searchText = text.Substring(start, end - start);
        var sentenceEndings = new[] { ".", "!", "?", "\n\n" };

        var lastIndex = -1;
        foreach (var ending in sentenceEndings) {
            var index = searchText.LastIndexOf(ending, StringComparison.Ordinal);
            if (index > lastIndex) {
                lastIndex = index;
            }
        }

        return lastIndex > -1 ? start + lastIndex + 1 : -1;
    }

}
