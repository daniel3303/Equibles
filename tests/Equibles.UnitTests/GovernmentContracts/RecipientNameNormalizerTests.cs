using Equibles.GovernmentContracts.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

public class RecipientNameNormalizerTests
{
    [Theory]
    [InlineData("Lockheed Martin Corporation", "LOCKHEED MARTIN")]
    [InlineData("Lockheed Martin Corp", "LOCKHEED MARTIN")]
    [InlineData("Lockheed Martin Corp.", "LOCKHEED MARTIN")]
    [InlineData("The Boeing Company", "BOEING")]
    [InlineData("Raytheon Technologies, Inc.", "RAYTHEON TECHNOLOGIES")]
    [InlineData("Raytheon Technologies", "RAYTHEON TECHNOLOGIES")]
    [InlineData("International Business Machines Corporation", "INTERNATIONAL BUSINESS MACHINES")]
    public void Normalize_strips_suffixes_punctuation_and_leading_the(string input, string expected)
    {
        RecipientNameNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Two_legal_forms_of_the_same_name_collapse_to_one_key()
    {
        var a = RecipientNameNormalizer.Normalize("Lockheed Martin Corporation");
        var b = RecipientNameNormalizer.Normalize("LOCKHEED MARTIN CORP");
        a.Should().Be(b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Inc")]
    [InlineData("Co")]
    public void Normalize_returns_null_for_empty_or_suffix_only_input(string input)
    {
        RecipientNameNormalizer.Normalize(input).Should().BeNull();
    }

    [Fact]
    public void Multiple_trailing_suffixes_are_all_stripped()
    {
        RecipientNameNormalizer.Normalize("Acme Holdings Co LLC").Should().Be("ACME HOLDINGS");
    }
}
