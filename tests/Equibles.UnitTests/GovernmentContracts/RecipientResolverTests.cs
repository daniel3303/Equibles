using Equibles.GovernmentContracts.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

public class RecipientResolverTests
{
    [Fact]
    public void Resolve_MatchesByNormalizedName_NotRawString()
    {
        var stockId = Guid.NewGuid();
        // The lookup is keyed by the normalized company name, exactly as BuildLookup produces it.
        var lookup = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            [RecipientNameNormalizer.Normalize("Lockheed Martin Corporation")] = stockId,
        };

        // Contract: resolution is an exact match on the NORMALIZED key, so a differently
        // suffixed/punctuated recipient name still resolves — raw string equality would not.
        RecipientResolver.Resolve("LOCKHEED MARTIN CORP.", lookup).Should().Be(stockId);
    }
}
