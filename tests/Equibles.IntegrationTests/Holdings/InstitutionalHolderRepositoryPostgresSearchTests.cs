using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins <see cref="InstitutionalHolderRepository.Search"/> against real Postgres:
/// the production query uses <c>EF.Functions.ILike</c> for case-insensitive
/// substring matching on holder Name. A regression to case-sensitive
/// <c>EF.Functions.Like</c> (or to in-memory <c>.Contains</c>) would silently
/// drop hits on every non-exact-case query — and the holdings UI passes user
/// keystrokes through unchanged.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHolderRepositoryPostgresSearchTests : ParadeDbMcpTestBase
{
    public InstitutionalHolderRepositoryPostgresSearchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_LowercaseSubstringAcrossMultipleHolders_ReturnsOnlyTheMatchViaILike()
    {
        DbContext.Add(new InstitutionalHolder { Cik = "0001067983", Name = "Berkshire Hathaway Inc." });
        DbContext.Add(new InstitutionalHolder { Cik = "0001364742", Name = "BlackRock Inc." });
        DbContext.Add(new InstitutionalHolder { Cik = "0000102909", Name = "Vanguard Group Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHolderRepository(verify);

        // Lowercase substring against TitleCased Name — only the BlackRock row matches.
        // The match is also non-anchored ("rock" is in the middle of "BlackRock"), so
        // a regression that anchored the LIKE pattern (e.g. removed the wildcards)
        // would also fail here.
        var results = await sut.Search("rock").AsNoTracking().ToListAsync();

        results.Should().ContainSingle();
        results[0].Cik.Should().Be("0001364742");
    }
}
