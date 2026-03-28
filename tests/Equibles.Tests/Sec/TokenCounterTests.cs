using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.Tests.Sec;

public class TokenCounterTests {
    private readonly TokenCounter _sut = new();

    [Fact]
    public void CountTokens_NullInput_ReturnsZero() {
        var result = _sut.CountTokens(null);

        result.Should().Be(0);
    }

    [Fact]
    public void CountTokens_EmptyString_ReturnsZero() {
        var result = _sut.CountTokens(string.Empty);

        result.Should().Be(0);
    }

    [Fact]
    public void CountTokens_SingleWord_ReturnsPositiveCount() {
        var result = _sut.CountTokens("hello");

        result.Should().BePositive();
    }

    [Fact]
    public void CountTokens_Sentence_ReturnsCountGreaterThanOne() {
        var result = _sut.CountTokens("The quick brown fox jumps over the lazy dog.");

        result.Should().BeGreaterThan(1);
    }

    [Fact]
    public void CountTokens_LongText_ReturnsHigherCountThanShortText() {
        var shortText = "Hello world";
        var longText = "This is a much longer piece of text that should produce significantly more tokens than the short text above.";

        var shortCount = _sut.CountTokens(shortText);
        var longCount = _sut.CountTokens(longText);

        longCount.Should().BeGreaterThan(shortCount);
    }

    [Fact]
    public void Tokenizer_IsNotNull() {
        _sut.Tokenizer.Should().NotBeNull();
    }

    [Fact]
    public void CountTokens_SameInput_ReturnsConsistentResults() {
        var text = "Consistency is key in tokenization.";

        var firstCount = _sut.CountTokens(text);
        var secondCount = _sut.CountTokens(text);
        var thirdCount = _sut.CountTokens(text);

        firstCount.Should().Be(secondCount);
        secondCount.Should().Be(thirdCount);
    }
}
