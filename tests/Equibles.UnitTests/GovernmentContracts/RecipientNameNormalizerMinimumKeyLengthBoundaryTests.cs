using Equibles.GovernmentContracts.HostedService.Services;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: the minimum key length is 2 — matching is exact and the resolver drops
// ambiguous keys, so short DISTINCTIVE names are safe, and the previous 4-char floor was
// silently excluding real defense primes (RTX, 3M) from resolution entirely (#7044's root
// cause alongside subsidiary naming). Only single-character keys are dropped: one letter
// carries no identity.
public class RecipientNameNormalizerMinimumKeyLengthBoundaryTests
{
    [Fact]
    public void Normalize_KeyExactlyAtMinimumLength_IsReturned()
    {
        // "3M Co" strips the CO suffix to "3M" (2 chars) — the inclusive lower edge.
        // Guards >= against regressing to >, which would re-drop 3M.
        RecipientNameNormalizer.Normalize("3M Co").Should().Be("3M");
    }

    [Theory]
    [InlineData("RTX Corp", "RTX")]
    [InlineData("V2X, Inc.", "V2X")]
    [InlineData("Aar Corp", "AAR")]
    public void Normalize_ShortDistinctivePrimeNames_Resolve(string name, string expectedKey)
    {
        RecipientNameNormalizer.Normalize(name).Should().Be(expectedKey);
    }

    [Fact]
    public void Normalize_SingleCharacterKey_IsDropped()
    {
        RecipientNameNormalizer.Normalize("X Inc").Should().BeNull();
    }
}
