using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Existing <c>InsiderOwnerRepositoryTests</c> in
/// <c>InsiderTradingRepositoryTests.cs</c> explicitly excludes Search because
/// it depends on <c>EF.Functions.ILike</c> against real Postgres. This pins
/// the case-insensitive substring behaviour the insider-trading search box
/// relies on — a regression to case-sensitive Like or to <c>.Contains</c>
/// would silently miss results on every non-exact-case query.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderOwnerRepositoryPostgresSearchTests : ParadeDbMcpTestBase
{
    public InsiderOwnerRepositoryPostgresSearchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_LowercaseSubstringAgainstTitleCasedName_ReturnsMatchViaILike()
    {
        DbContext.Add(new InsiderOwner { OwnerCik = "1", Name = "Cook, Timothy D." });
        DbContext.Add(new InsiderOwner { OwnerCik = "2", Name = "Pichai, Sundar" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InsiderOwnerRepository(verify);

        // Lower-case substring against TitleCased name. Choosing "tim" — appears
        // in "Timothy" (substring) but neither owner has "tim" with matching case.
        var results = await sut.Search("tim").AsNoTracking().ToListAsync();

        results.Should().ContainSingle();
        results[0].OwnerCik.Should().Be("1");
    }

    [Fact]
    public async Task Search_ReversedNameOrder_MatchesAllTokens()
    {
        DbContext.Add(new InsiderOwner { OwnerCik = "1", Name = "Musk Elon" });
        DbContext.Add(new InsiderOwner { OwnerCik = "2", Name = "Cook Timothy D" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InsiderOwnerRepository(verify);

        var results = await sut.Search("Elon Musk").AsNoTracking().ToListAsync();

        results.Should().ContainSingle();
        results[0].OwnerCik.Should().Be("1");
    }

    [Fact]
    public async Task Search_CrossTokensFromDifferentNames_ReturnsEmpty()
    {
        // "Elon" lives in "Musk Elon", "Cook" lives in "Cook Timothy D" — but no
        // single record contains both tokens, so AND semantics must yield zero rows.
        DbContext.Add(new InsiderOwner { OwnerCik = "1", Name = "Musk Elon" });
        DbContext.Add(new InsiderOwner { OwnerCik = "2", Name = "Cook Timothy D" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InsiderOwnerRepository(verify);

        var results = await sut.Search("Elon Cook").AsNoTracking().ToListAsync();

        results.Should().BeEmpty();
    }
}
