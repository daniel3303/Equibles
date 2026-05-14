using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Pins <see cref="CongressMemberRepository.Search"/>: the production query uses
/// <c>EF.Functions.ILike</c> (Postgres case-insensitive LIKE) so a lower-cased
/// substring matches a TitleCased stored name. A regression that swapped to
/// <c>EF.Functions.Like</c> (case-sensitive) or to <c>.Contains</c> on the
/// in-memory side would silently miss matches on every non-exact-case query.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressMemberRepositoryPostgresSearchTests : ParadeDbMcpTestBase
{
    public CongressMemberRepositoryPostgresSearchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_LowercaseSubstringAgainstTitleCasedName_ReturnsMatchViaILike()
    {
        DbContext.Add(new CongressMember { Name = "Pelosi, Nancy", Position = CongressPosition.Representative });
        DbContext.Add(new CongressMember { Name = "Tuberville, Tommy", Position = CongressPosition.Senator });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new CongressMemberRepository(verify);

        // Lowercase query against TitleCased seed names — would fail under
        // case-sensitive LIKE. Both results show this is case-insensitive AND
        // a substring (not whole-word) match.
        var results = await sut.Search("nancy").ToListAsync();

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Pelosi, Nancy");
    }
}
