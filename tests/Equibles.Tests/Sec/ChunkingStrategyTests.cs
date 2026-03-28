using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.Tests.Sec;

public class ChunkingStrategyTests {
    private readonly ChunkingStrategy _strategy;

    public ChunkingStrategyTests() {
        var tokenCounter = new TokenCounter();
        _strategy = new ChunkingStrategy(tokenCounter);
    }

    [Fact]
    public void SplitIntoChunks_NullInput_ReturnsEmptyList() {
        var result = _strategy.SplitIntoChunks(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void SplitIntoChunks_EmptyString_ReturnsEmptyList() {
        var result = _strategy.SplitIntoChunks(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void SplitIntoChunks_ShortText_ReturnsSingleChunk() {
        var text = "This is a short piece of text that fits within a single chunk.";

        var result = _strategy.SplitIntoChunks(text);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void SplitIntoChunks_LongText_ReturnsMultipleChunks() {
        var text = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 300));

        var result = _strategy.SplitIntoChunks(text);

        result.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void SplitIntoChunks_FirstChunk_HasStartPositionZero() {
        var text = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 300));

        var result = _strategy.SplitIntoChunks(text);

        result.First().StartPosition.Should().Be(0);
    }

    [Fact]
    public void SplitIntoChunks_AllChunks_HaveNonEmptyContent() {
        var text = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 300));

        var result = _strategy.SplitIntoChunks(text);

        result.Should().AllSatisfy(chunk => chunk.Content.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void SplitIntoChunks_ChunksHaveCorrectStartLineNumber() {
        var text = "Line one.\nLine two.\nLine three.\nLine four.";

        var result = _strategy.SplitIntoChunks(text);

        result.First().StartLineNumber.Should().Be(1);
    }

    [Fact]
    public void SplitIntoChunks_MultilineText_StartLineNumberReflectsNewlines() {
        var lines = Enumerable.Range(1, 500).Select(i => $"This is sentence number {i} in a very long document.");
        var text = string.Join("\n", lines);

        var result = _strategy.SplitIntoChunks(text);

        // First chunk starts at line 1
        result.First().StartLineNumber.Should().Be(1);

        // If there are multiple chunks, later chunks should have higher line numbers
        if (result.Count > 1) {
            result.Last().StartLineNumber.Should().BeGreaterThan(1);
        }
    }

    [Fact]
    public void CleanText_HtmlTags_StripsTagsAndReturnsPlainText() {
        var result = _strategy.CleanText("<p>hello</p>");

        result.Should().Be("hello");
    }

    [Fact]
    public void CleanText_ExcessiveWhitespace_NormalizesToSingleSpaces() {
        var result = _strategy.CleanText("hello   world");

        result.Should().Be("hello world");
    }

    [Fact]
    public void CleanText_NullInput_ReturnsEmptyString() {
        var result = _strategy.CleanText(null);

        result.Should().BeEmpty();
    }
}
