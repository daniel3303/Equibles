using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins the member-name normalisation in <c>HouseDisclosureClient</c> (the PTR /
/// trades path) for the duplicate sets reported in #3374. House filings inject
/// honorific tokens into the disclosed name — often without the trailing period
/// the old <c>"Mr. "</c> replace looked for, and "Dr" was never handled at all —
/// and occasionally double the first name ("Scott Scott Franklin"). Each variant
/// must collapse to the same canonical name so one member maps to one
/// <c>CongressMember</c> record instead of two-to-four.
/// </summary>
public class HouseDisclosureClientStripHonorificPrefixesVariantTests
{
    private static readonly MethodInfo StripHonorificPrefixesMethod =
        typeof(HouseDisclosureClient).GetMethod(
            "StripHonorificPrefixes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Theory]
    // Period-less honorific, leading and mid-name (was never stripped).
    [InlineData("Mr Matt Rosendale", "Matt Rosendale")]
    [InlineData("Matt Mr Rosendale", "Matt Rosendale")]
    // "Dr" was absent from the honorific list entirely.
    [InlineData("Mark Dr Green", "Mark Green")]
    [InlineData("Kim Dr Schrier", "Kim Schrier")]
    [InlineData("Marjorie Taylor Mrs Greene", "Marjorie Taylor Greene")]
    [InlineData("Scott Mr Franklin", "Scott Franklin")]
    // Doubled first name from the parser.
    [InlineData("Scott Scott Franklin", "Scott Franklin")]
    // Existing stacked-prefix contract (#1422) still holds.
    [InlineData("Hon. Mr. Smith", "Smith")]
    // Clean names and genuine initials are left untouched (no over-merging).
    [InlineData("Scott Franklin", "Scott Franklin")]
    [InlineData("C. Scott Franklin", "C. Scott Franklin")]
    public void StripHonorificPrefixes_NameVariant_NormalisesToCanonical(
        string input,
        string expected
    )
    {
        var result = (string)StripHonorificPrefixesMethod.Invoke(null, [input]);

        result.Should().Be(expected);
    }
}
