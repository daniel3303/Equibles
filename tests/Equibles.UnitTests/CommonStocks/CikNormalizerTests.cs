using Equibles.CommonStocks.Data.Helpers;

namespace Equibles.UnitTests.CommonStocks;

public class CikNormalizerTests
{
    [Theory]
    [InlineData(" 0000036405 ", "0000036405", "36405")]
    [InlineData("10", "10", "10")]
    public void ValidAsciiCik_PreservesLookupSpellingAndCanonicalizesIdentity(
        string input,
        string validated,
        string canonical
    )
    {
        CikNormalizer.Validate(input).Should().Be(validated);
        CikNormalizer.Canonicalize(input).Should().Be(canonical);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("not-a-cik")]
    [InlineData("١٢٣")]
    [InlineData("12345678901234567")]
    public void InvalidCik_FailsClosed(string input)
    {
        CikNormalizer.Validate(input).Should().BeNull();
        CikNormalizer.Canonicalize(input).Should().BeNull();
    }
}
