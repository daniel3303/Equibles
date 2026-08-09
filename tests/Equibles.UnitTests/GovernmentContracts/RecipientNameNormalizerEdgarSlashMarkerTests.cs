using Equibles.GovernmentContracts.HostedService.Services;

namespace Equibles.UnitTests.GovernmentContracts;

// EDGAR registrant names carry a slash-wrapped state-of-incorporation or vintage marker
// ("/De/", "/Pa/", "/New/") that USAspending recipient/parent names never do. Left in
// place, the marker survives suffix-stripping as a stray trailing token ("CACI
// INTERNATIONAL INC DE") and defeats the exact match against the award side's "CACI
// INTERNATIONAL INC" — which is how NOC, LHX and CACI awards resolved to nothing (#7044).
// Removing the marker is a mechanical format rule; matching stays exact on what remains.
public class RecipientNameNormalizerEdgarSlashMarkerTests
{
    [Theory]
    [InlineData("Caci International Inc /De/", "CACI INTERNATIONAL")]
    [InlineData("Northrop Grumman Corp /De/", "NORTHROP GRUMMAN")]
    [InlineData("L3harris Technologies, Inc. /De/", "L3HARRIS TECHNOLOGIES")]
    // No spaces around the marker — "Fnb Corp/Pa/" is a real stored shape.
    [InlineData("Fnb Corp/Pa/", "FNB")]
    // Non-state vintage marker.
    [InlineData("Nve Corp /New/", "NVE")]
    public void Normalize_StripsEdgarIncorporationMarkers(string name, string expectedKey)
    {
        RecipientNameNormalizer.Normalize(name).Should().Be(expectedKey);
    }

    [Fact]
    public void Normalize_BothSidesOfTheMatchCollapseToTheSameKey()
    {
        // The stored stock name (EDGAR shape) and the USAspending parent name must land on
        // one key — this equality IS the match the import performs.
        RecipientNameNormalizer
            .Normalize("Caci International Inc /De/")
            .Should()
            .Be(RecipientNameNormalizer.Normalize("CACI INTERNATIONAL INC"));
    }

    [Fact]
    public void Normalize_InteriorSlashes_AreNeverMangledByTheMarkerRule()
    {
        // The marker regex is anchored to the END of the name; interior short slash-wrapped
        // segments (an unanchored pattern would pair "AB/CD EF/GH"'s inner slashes and
        // silently drop "CD EF") keep the pre-existing slash-becomes-space behaviour.
        RecipientNameNormalizer.Normalize("AB/CD EF/GH Corp").Should().Be("AB CD EF GH");
    }

    [Fact]
    public void Normalize_SingleSlashSuffix_IsUntouchedByTheMarkerRule()
    {
        // "Slb Limited/Nv" has no closing slash, so the marker regex must not fire; the
        // slash still splits tokens and NV/LIMITED strip as legal suffixes.
        RecipientNameNormalizer.Normalize("Slb Limited/Nv").Should().Be("SLB");
    }
}
