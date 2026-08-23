using Equibles.Congress.Data.Models;
using Xunit;

namespace Equibles.UnitTests.Congress;

public class CongressMemberIdentityCatalogTests
{
    [Theory]
    [InlineData("James Banks", "B001299", "James Banks")]
    [InlineData("James E Banks", "B001299", "James Banks")]
    [InlineData("James E. Banks", "B001299", "James Banks")]
    [InlineData("Jim Banks", "B001299", "James Banks")]
    [InlineData("Adam B Schiff", "S001150", "Adam Schiff")]
    [InlineData("Adam B. Schiff", "S001150", "Adam Schiff")]
    [InlineData("C. Scott Franklin", "F000472", "Scott Franklin")]
    public void Resolve_ReviewedExactAlias_ReturnsStableIdentity(
        string alias,
        string bioguideId,
        string canonicalName
    )
    {
        var result = CongressMemberIdentityCatalog.Resolve(alias);

        result.Should().NotBeNull();
        result.BioguideId.Should().Be(bioguideId);
        result.CanonicalName.Should().Be(canonicalName);
    }

    [Theory]
    [InlineData("J. Banks")]
    [InlineData("Robert Goodman")]
    [InlineData("Scott F Franklin")]
    public void Resolve_UnreviewedSimilarName_RefusesToInferIdentity(string name)
    {
        CongressMemberIdentityCatalog.Resolve(name).Should().BeNull();
    }

    [Fact]
    public void All_AliasesAndBioguideIds_AreUnambiguous()
    {
        var identities = CongressMemberIdentityCatalog.All;

        identities.Select(identity => identity.BioguideId).Should().OnlyHaveUniqueItems();
        var aliases = identities.SelectMany(identity => identity.Aliases).ToList();
        aliases.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveSameCount(aliases);
    }
}
