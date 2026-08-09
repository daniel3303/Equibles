using Equibles.GovernmentContracts.HostedService.Services;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: parent-fallback resolution goes through the SAME exact normalised lookup as
// direct matching, and holds only when every resolving candidate lands on ONE stock — a
// recipient whose registered parents resolve to different companies is ambiguous and must
// never be linked.
public class RecipientResolverResolveViaParentsTests
{
    private static readonly Guid CaciId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    private static IReadOnlyDictionary<string, Guid> Lookup() =>
        new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["CACI INTERNATIONAL"] = CaciId,
            ["LOCKHEED MARTIN"] = OtherId,
        };

    [Fact]
    public void ResolveViaParents_SingleMatchingParent_Resolves()
    {
        RecipientResolver
            .ResolveViaParents(["CACI International Inc"], Lookup())
            .Should()
            .Be(CaciId);
    }

    [Fact]
    public void ResolveViaParents_NoParentMatches_ReturnsNull()
    {
        RecipientResolver
            .ResolveViaParents(["Privately Held Parent LLC"], Lookup())
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveViaParents_ParentsResolveToDifferentStocks_IsAmbiguousAndDropped()
    {
        RecipientResolver
            .ResolveViaParents(["CACI International Inc", "Lockheed Martin Corp"], Lookup())
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveViaParents_ParentsAgreeOnOneStock_Resolves()
    {
        // Two name variants (a rename in the registration history) that land on the same
        // stock are agreement, not ambiguity.
        RecipientResolver
            .ResolveViaParents(["CACI International Inc", "CACI INTERNATIONAL"], Lookup())
            .Should()
            .Be(CaciId);
    }

    [Fact]
    public void ResolveViaParents_NullAndUnresolvableNamesAreSkipped()
    {
        RecipientResolver
            .ResolveViaParents([null, "The Company", "CACI International Inc"], Lookup())
            .Should()
            .Be(CaciId);
    }
}
