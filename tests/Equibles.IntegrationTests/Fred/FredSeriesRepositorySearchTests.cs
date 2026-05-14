using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// Pins <see cref="FredSeriesRepository.Search"/>: the production query lowercases
/// both sides and matches the query against EITHER SeriesId OR Title. Two real
/// regression surfaces here — (a) dropping the SeriesId branch (users typing
/// "GDP" would only match Titles containing it, missing the literal SeriesId
/// row), and (b) reverting to case-sensitive Contains (the front-end search box
/// passes the user's literal casing).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FredSeriesRepositorySearchTests : ParadeDbMcpTestBase
{
    public FredSeriesRepositorySearchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_LowercaseQueryAgainstUppercaseSeriesId_MatchesViaSeriesIdBranchCaseInsensitive()
    {
        DbContext.Add(new FredSeries
        {
            SeriesId = "GDP",
            Title = "Gross Domestic Product",
        });
        DbContext.Add(new FredSeries
        {
            SeriesId = "UNRATE",
            Title = "Unemployment Rate",
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new FredSeriesRepository(verify);

        // Query "gdp" must match SeriesId "GDP" via the lowercased SeriesId.Contains
        // branch — a regression that dropped the SeriesId.ToLower() branch and only
        // kept Title would still match this row (Title contains "Domestic"), so
        // the assertion would silently pass. Pin with "gdp" — Title does NOT
        // contain "gdp" as a substring (case-insensitive), so a match here can
        // only come from the SeriesId branch.
        var results = await sut.Search("gdp").AsNoTracking().ToListAsync();

        results.Should().ContainSingle();
        results[0].SeriesId.Should().Be("GDP");
    }
}
