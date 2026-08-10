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
/// the case-insensitive whole-word behavior the insider discovery contract requires.
/// Substrings inside a different filed-name word must not inflate totals.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderOwnerRepositoryPostgresSearchTests : ParadeDbMcpTestBase
{
    public InsiderOwnerRepositoryPostgresSearchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_LowercaseWordAgainstTitleCasedName_ReturnsMatch()
    {
        DbContext.Add(new InsiderOwner { OwnerCik = "1", Name = "Cook, Timothy D." });
        DbContext.Add(new InsiderOwner { OwnerCik = "2", Name = "Pichai, Sundar" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InsiderOwnerRepository(verify);

        var results = await sut.Search("timothy").AsNoTracking().ToListAsync();

        results.Should().ContainSingle();
        results[0].OwnerCik.Should().Be("1");
    }

    [Fact]
    public async Task Search_SubstringInsideDifferentWord_DoesNotMatch()
    {
        DbContext.AddRange(
            new InsiderOwner { OwnerCik = "1", Name = "Joanna Smith" },
            new InsiderOwner { OwnerCik = "2", Name = "Ann Jones" }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new InsiderOwnerRepository(verify)
            .Search("ann")
            .AsNoTracking()
            .ToListAsync();

        results.Should().ContainSingle().Which.OwnerCik.Should().Be("2");
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
    public async Task Search_AllTokenMatchExists_DoesNotIncludeAnyTokenOnlyRows()
    {
        DbContext.AddRange(
            new InsiderOwner { OwnerCik = "1", Name = "Musk Elon" },
            new InsiderOwner { OwnerCik = "2", Name = "Cook Timothy D" },
            new InsiderOwner { OwnerCik = "3", Name = "Cook Elon" }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InsiderOwnerRepository(verify);

        var results = await sut.Search("Elon Cook").AsNoTracking().ToListAsync();

        results.Should().ContainSingle().Which.OwnerCik.Should().Be("3");
    }

    [Fact]
    public async Task Search_NoAllTokenMatch_BroadensToAnyWholeWord()
    {
        DbContext.AddRange(
            new InsiderOwner { OwnerCik = "1", Name = "Warren Buffett" },
            new InsiderOwner { OwnerCik = "2", Name = "Joanna Current" }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new InsiderOwnerRepository(verify)
            .Search("current warren buffett")
            .AsNoTracking()
            .ToListAsync();

        results.Select(o => o.OwnerCik).Should().BeEquivalentTo("1", "2");
    }

    [Fact]
    public async Task Search_ExactStoredName_OutranksVerifiedAlias()
    {
        DbContext.AddRange(
            new InsiderOwner { OwnerCik = "exact", Name = "Jensen Huang" },
            new InsiderOwner { OwnerCik = "0001197649", Name = "HUANG JEN HSUN" }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new InsiderOwnerRepository(verify)
            .Search("Jensen Huang")
            .AsNoTracking()
            .ToListAsync();

        results.Should().ContainSingle().Which.OwnerCik.Should().Be("exact");
    }
}
