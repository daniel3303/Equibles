using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// Pins that <see cref="FredSeriesRepository.Search"/> treats a typed underscore
/// as a LITERAL character, not a single-char LIKE wildcard — the same defect
/// class filed for the Congress / Insider / Institution / Status searches. A
/// refactor from the wildcard-safe <c>.Contains</c> to a hand-built
/// <c>EF.Functions.Like("%" + q + "%")</c> would silently match "INFLATIONXINDEX"
/// for the query "inflation_index"; this guards against that regression.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FredSeriesRepositorySearchWildcardTests : ParadeDbMcpTestBase
{
    public FredSeriesRepositorySearchWildcardTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_QueryWithUnderscore_TreatsUnderscoreAsLiteralNotWildcard()
    {
        DbContext.Add(new FredSeries { SeriesId = "S1", Title = "INFLATION_INDEX" });
        DbContext.Add(new FredSeries { SeriesId = "S2", Title = "INFLATIONXINDEX" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new FredSeriesRepository(verify);

        // The underscore must match literally: only "INFLATION_INDEX" qualifies.
        // If '_' were a wildcard, "INFLATIONXINDEX" (X in the underscore slot)
        // would also match — the bug this pin forbids.
        var results = await sut.Search("inflation_index").AsNoTracking().ToListAsync();

        results.Should().ContainSingle();
        results[0].SeriesId.Should().Be("S1");
    }
}
