using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the SEC company-name casing rule applied at sync time: ALL-CAPS feed
/// values are title-cased on the way in, mixed-case names are trusted as-is.
/// Tested via reflection because the helper is private to CompanySyncService.
/// </summary>
public class CompanySyncServiceNormalizeCompanyNameTests
{
    private static readonly MethodInfo NormalizeMethod =
        typeof(CompanySyncService).GetMethod(
            "NormalizeCompanyName",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static string Normalize(string name) =>
        (string)NormalizeMethod.Invoke(null, [name]);

    [Theory]
    [InlineData("AMAZON COM INC", "Amazon Com Inc")]
    [InlineData("MICROSOFT CORP", "Microsoft Corp")]
    [InlineData("NVIDIA CORP", "Nvidia Corp")]
    [InlineData("BERKSHIRE HATHAWAY INC", "Berkshire Hathaway Inc")]
    public void AllCapsName_IsTitleCased(string input, string expected)
    {
        Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Apple Inc.")]
    [InlineData("Alphabet Inc.")]
    [InlineData("Meta Platforms, Inc.")]
    [InlineData("JPMorgan Chase & Co.")]
    public void MixedCaseName_IsLeftAlone(string input)
    {
        Normalize(input).Should().Be(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespace_IsLeftAlone(string input)
    {
        Normalize(input).Should().Be(input);
    }

    [Fact]
    public void SingleCapitalLetter_IsTitleCased()
    {
        // Defensive: a one-character ticker-as-name shouldn't be left
        // shouting just because there's no lowercase letter to detect.
        Normalize("X").Should().Be("X");
    }
}
