using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// Pins the punctuation-independent token contract: an underscore separates words and never
/// becomes a SQL wildcard. Every resulting word must still occur in the row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FredSeriesRepositorySearchWildcardTests : ParadeDbMcpTestBase
{
    public FredSeriesRepositorySearchWildcardTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_QueryWithUnderscore_UsesBothTokensWithoutSqlWildcardSemantics()
    {
        DbContext.Add(new FredSeries { SeriesId = "S1", Title = "INFLATION_INDEX" });
        DbContext.Add(new FredSeries { SeriesId = "S2", Title = "INFLATIONXINDEX" });
        DbContext.Add(new FredSeries { SeriesId = "S3", Title = "INFLATION RATE" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new FredSeriesRepository(verify);

        var results = await sut.Search("inflation_index").AsNoTracking().ToListAsync();

        results.Select(s => s.SeriesId).Should().BeEquivalentTo("S1", "S2");
    }
}
