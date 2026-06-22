using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins the centralised member-name normaliser
/// (<see cref="DisclosureParsingHelper.NormalizeMemberName"/>) for the duplicate
/// sets reported in #3374. Every disclosure source (House XML/PDF, Senate) and
/// the <c>CongressMember</c> upsert key run through this single method, so a
/// member resolves to one record no matter which scraper emitted the name.
///
/// Filings inject honorific tokens mid-name — often without the trailing period
/// — and occasionally double the first name; each malformed rendering must
/// collapse to the same canonical name, while clean names and genuine initials
/// pass through untouched (no over-merging of distinct people).
/// </summary>
public class DisclosureParsingHelperNormalizeMemberNameTests
{
    [Theory]
    // Honorific injected mid-name (period-less and period-bearing).
    [InlineData("Scott Mr Franklin", "Scott Franklin")]
    [InlineData("Marjorie Taylor Mrs Greene", "Marjorie Taylor Greene")]
    [InlineData("Mark Dr Green", "Mark Green")]
    [InlineData("Kim Dr Schrier", "Kim Schrier")]
    [InlineData("Matt Mr Rosendale", "Matt Rosendale")]
    [InlineData("Mr Matt Rosendale", "Matt Rosendale")]
    [InlineData("Hon. Mr. Smith", "Smith")]
    // Doubled first name emitted by the parser.
    [InlineData("Scott Scott Franklin", "Scott Franklin")]
    // Stray whitespace collapses.
    [InlineData("  Scott   Franklin  ", "Scott Franklin")]
    // Clean names and genuine initials are left untouched.
    [InlineData("Scott Franklin", "Scott Franklin")]
    [InlineData("C. Scott Franklin", "C. Scott Franklin")]
    [InlineData("Marjorie Taylor Greene", "Marjorie Taylor Greene")]
    // A real surname that merely starts with an honorific stays intact.
    [InlineData("Jason Mraz", "Jason Mraz")]
    public void NormalizeMemberName_NameVariant_NormalisesToCanonical(string input, string expected)
    {
        DisclosureParsingHelper.NormalizeMemberName(input).Should().Be(expected);
    }
}
