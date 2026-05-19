using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.InsiderTrading.Repositories.Search;
using Equibles.IntegrationTests.Helpers;
using Equibles.Search.Abstractions;
using Xunit;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// InsiderOwnerSearchProvider's DescribeRole is unit-tested, but its end-to-end
/// pipeline (Filter → repository ILike Search → Materialize → Project into a
/// SearchHit) only runs against real Postgres and was uncovered. Pins that a
/// matching owner is projected with the global-search contract the Web layer
/// relies on: Title=name, Kind="Insider", Subtitle=role, ownerCik route value,
/// under the provider's declared Category/Order.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderOwnerSearchProviderTests : ParadeDbMcpTestBase
{
    public InsiderOwnerSearchProviderTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_MatchingOwner_ProjectsSearchHitWithRoleAndRoute()
    {
        DbContext.Add(
            new InsiderOwner
            {
                OwnerCik = "0000320193",
                Name = "Cook, Timothy D.",
                OfficerTitle = "Chief Executive Officer",
            }
        );
        DbContext.Add(new InsiderOwner { OwnerCik = "9", Name = "Pichai, Sundar" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InsiderOwnerSearchProvider(new InsiderOwnerRepository(verify));

        var group = await sut.Search(
            new SearchRequest { Query = "cook", MaxPerProvider = 10 },
            CancellationToken.None
        );

        group.Category.Should().Be("Insiders");
        group.Order.Should().Be(40);
        var hit = group.Hits.Should().ContainSingle().Subject;
        hit.Title.Should().Be("Cook, Timothy D.");
        hit.Kind.Should().Be("Insider");
        hit.Subtitle.Should().Be("Chief Executive Officer");
        hit.RouteValues.Should().ContainKey("ownerCik").WhoseValue.Should().Be("0000320193");
    }
}
