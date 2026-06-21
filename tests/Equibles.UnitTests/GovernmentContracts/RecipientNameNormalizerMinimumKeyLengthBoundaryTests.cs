using Equibles.GovernmentContracts.HostedService.Services;

namespace Equibles.UnitTests.GovernmentContracts;

public class RecipientNameNormalizerMinimumKeyLengthBoundaryTests
{
    [Fact]
    public void Normalize_KeyExactlyAtMinimumLength_IsReturned()
    {
        // Contract: a key shorter than the 4-char minimum is dropped to null, so a key of
        // EXACTLY 4 chars is the inclusive lower edge and must be kept. "Ford Inc" strips the
        // INC suffix to "FORD" (4 chars). Guards >= against regressing to >, which would drop
        // legitimate four-letter recipient names from exact-match resolution.
        RecipientNameNormalizer.Normalize("Ford Inc").Should().Be("FORD");
    }
}
