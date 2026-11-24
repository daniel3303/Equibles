using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategySplitIntoChunksPositionTests
{
    private readonly ChunkingStrategy _strategy = new(new TokenCounter());

    [Fact]
    public void SplitIntoChunks_EveryChunk_IsTheTextBetweenItsPositionsOnItsLine()
    {
        var text = string.Join(
            "\n",
            Enumerable
                .Range(1, 1200)
                .Select(line => $"Line {line}: revenue rose to {line * 37} million. Costs fell.")
        );

        var result = _strategy.SplitIntoChunks(text);

        result.Should().HaveCountGreaterThan(3);
        foreach (var chunk in result)
        {
            var between = text.Substring(
                chunk.StartPosition,
                chunk.EndPosition - chunk.StartPosition
            );
            chunk.Content.Should().Be(_strategy.CleanText(between));
            chunk
                .StartLineNumber.Should()
                .Be(text.Take(chunk.StartPosition).Count(character => character == '\n') + 1);
        }
    }

    [Fact]
    public void SplitIntoChunks_BoundaryInsideACharacterTheTokenizerSplits_KeepsTheCharacterWhole()
    {
        // The tokenizer spells each of these characters with several tokens, so a window of a
        // fixed token count ends inside one. A chunk decoded from such a window carried the
        // replacement character in place of the half it held.
        int[] rare = [0x2070E, 0x1D5C, 0x09FA, 0x1D11E, 0x36D1, 0x1C9F, 0x10348, 0x186F, 0x0D60];
        var text = string.Concat(
            Enumerable
                .Range(0, 9000)
                .Select(index =>
                    char.ConvertFromUtf32(rare[index % rare.Length])
                    + (index % 50 == 49 ? ". " : "")
                )
        );

        var result = _strategy.SplitIntoChunks(text);

        result.Should().HaveCountGreaterThan(1);
        result.Should().OnlyContain(chunk => !chunk.Content.Contains('�'));
        result
            .Should()
            .OnlyContain(chunk =>
                !char.IsLowSurrogate(chunk.Content.First())
                && !char.IsHighSurrogate(chunk.Content.Last())
            );
    }
}
