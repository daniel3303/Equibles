using Equibles.Data;

namespace Equibles.UnitTests.Data;

public class SearchTermsTests
{
    [Fact]
    public void Tokenize_IgnoresPunctuationOrderAndDuplicateSpelling()
    {
        SearchTerms.Tokenize("  Grantham, Mayo / GRANTHAM  ").Should().Equal("grantham", "mayo");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" - / & ")]
    public void Tokenize_NoWords_ReturnsEmpty(string query)
    {
        SearchTerms.Tokenize(query).Should().BeEmpty();
    }

    [Fact]
    public void Normalize_PreservesLettersAndDigitsAsWords()
    {
        SearchTerms.Normalize("E-mini S&P 500").Should().Be("e mini s p 500");
    }

    [Fact]
    public void WithSparseAnyTokenFallback_StrictRowsExist_ReturnsOnlyStrictRows()
    {
        var strict = new[] { "strict" }.AsQueryable();
        var broad = new[] { "strict", "broad" }.AsQueryable();

        SearchTerms.WithSparseAnyTokenFallback(strict, broad).Should().Equal("strict");
    }

    [Fact]
    public void WithSparseAnyTokenFallback_StrictRowsMissing_ReturnsDistinctBroadRows()
    {
        var strict = Array.Empty<string>().AsQueryable();
        var broad = new[] { "broad", "broad", "other" }.AsQueryable();

        SearchTerms.WithSparseAnyTokenFallback(strict, broad).Should().Equal("broad", "other");
    }

    [Fact]
    public void WithExclusiveResolutionTiers_ExactIdentifierExists_ReturnsOnlyExactRows()
    {
        var result = SearchTerms.WithExclusiveResolutionTiers(
            new[] { "exact" }.AsQueryable(),
            new[] { "alias" }.AsQueryable(),
            new[] { "strict" }.AsQueryable(),
            new[] { "broad" }.AsQueryable()
        );

        result.Should().Equal("exact");
    }

    [Fact]
    public void WithExclusiveResolutionTiers_ExactMissing_ReturnsOnlyAliasRows()
    {
        var result = SearchTerms.WithExclusiveResolutionTiers(
            Array.Empty<string>().AsQueryable(),
            new[] { "alias" }.AsQueryable(),
            new[] { "strict" }.AsQueryable(),
            new[] { "broad" }.AsQueryable()
        );

        result.Should().Equal("alias");
    }

    [Fact]
    public void WithExclusiveResolutionTiers_ExactAndAliasMissing_ReturnsOnlyStrictRows()
    {
        var result = SearchTerms.WithExclusiveResolutionTiers(
            Array.Empty<string>().AsQueryable(),
            Array.Empty<string>().AsQueryable(),
            new[] { "strict" }.AsQueryable(),
            new[] { "broad" }.AsQueryable()
        );

        result.Should().Equal("strict");
    }

    [Fact]
    public void WithExclusiveResolutionTiers_OnlyBroadRowsExist_ReturnsDistinctBroadRows()
    {
        var result = SearchTerms.WithExclusiveResolutionTiers(
            Array.Empty<string>().AsQueryable(),
            Array.Empty<string>().AsQueryable(),
            Array.Empty<string>().AsQueryable(),
            new[] { "broad", "broad", "other" }.AsQueryable()
        );

        result.Should().Equal("broad", "other");
    }
}
