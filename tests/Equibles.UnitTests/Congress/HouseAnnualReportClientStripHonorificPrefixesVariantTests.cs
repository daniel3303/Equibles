using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins the member-name normalisation in <c>HouseAnnualReportClient</c> (the
/// annual Form A / net-worth path that feeds the richest ranking) for the
/// duplicate sets reported in #3374. The annual path shares the same honorific
/// and doubled-token defects as the PTR path, so it must collapse the same
/// variants to one canonical name — otherwise a member is listed twice on
/// <c>/congress/richest</c>.
/// </summary>
public class HouseAnnualReportClientStripHonorificPrefixesVariantTests
{
    private static readonly MethodInfo StripHonorificPrefixesMethod =
        typeof(HouseAnnualReportClient).GetMethod(
            "StripHonorificPrefixes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Theory]
    [InlineData("Mr Matt Rosendale", "Matt Rosendale")]
    [InlineData("Matt Mr Rosendale", "Matt Rosendale")]
    [InlineData("Mark Dr Green", "Mark Green")]
    [InlineData("Marjorie Taylor Mrs Greene", "Marjorie Taylor Greene")]
    [InlineData("Scott Scott Franklin", "Scott Franklin")]
    [InlineData("Hon. Mr. Smith", "Smith")]
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
