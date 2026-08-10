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
        DbContext.Add(new FredSeries { SeriesId = "GDP", Title = "Gross Domestic Product" });
        DbContext.Add(new FredSeries { SeriesId = "UNRATE", Title = "Unemployment Rate" });
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

    [Theory]
    [InlineData("fed funds rate", "FEDFUNDS")]
    [InlineData("jobless claims", "ICSA")]
    [InlineData("payrolls", "PAYEMS")]
    [InlineData("yield curve", "T10Y2Y")]
    [InlineData("core CPI", "CPILFESL")]
    public async Task Search_StandardMacroVocabulary_ResolvesTrackedSeries(
        string query,
        string expectedSeriesId
    )
    {
        DbContext.AddRange(
            new FredSeries { SeriesId = "FEDFUNDS", Title = "Federal Funds Effective Rate" },
            new FredSeries { SeriesId = "ICSA", Title = "Initial Claims" },
            new FredSeries { SeriesId = "PAYEMS", Title = "All Employees, Total Nonfarm" },
            new FredSeries
            {
                SeriesId = "T10Y2Y",
                Title =
                    "10-Year Treasury Constant Maturity Minus 2-Year Treasury Constant Maturity",
            },
            new FredSeries
            {
                SeriesId = "CPILFESL",
                Title =
                    "Consumer Price Index for All Urban Consumers: All Items Less Food and Energy",
            },
            new FredSeries
            {
                SeriesId = "DISTRACTOR",
                Title = "Fed Funds Rate Jobless Claims Payrolls Yield Curve Core CPI",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new FredSeriesRepository(verify)
            .Search(query)
            .AsNoTracking()
            .ToListAsync();

        results.Should().ContainSingle().Which.SeriesId.Should().Be(expectedSeriesId);
    }

    [Fact]
    public async Task Search_ExactStoredSeriesId_OutranksVerifiedAlias()
    {
        DbContext.AddRange(
            new FredSeries { SeriesId = "PAYROLLS", Title = "Exact stored series" },
            new FredSeries { SeriesId = "PAYEMS", Title = "All Employees, Total Nonfarm" }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new FredSeriesRepository(verify)
            .Search("payrolls")
            .AsNoTracking()
            .ToListAsync();

        results.Should().ContainSingle().Which.SeriesId.Should().Be("PAYROLLS");
    }

    [Fact]
    public async Task Search_NoAllTokenMatch_BroadensToAnyToken()
    {
        DbContext.AddRange(
            new FredSeries { SeriesId = "ICSA", Title = "Initial Claims" },
            new FredSeries { SeriesId = "UNRATE", Title = "Unemployment Rate" }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new FredSeriesRepository(verify)
            .Search("current jobless claims")
            .AsNoTracking()
            .ToListAsync();

        results.Select(s => s.SeriesId).Should().Contain("ICSA");
    }

    [Fact]
    public async Task Search_AllTokenMatchExists_DoesNotIncludeAnyTokenOnlyRows()
    {
        DbContext.AddRange(
            new FredSeries { SeriesId = "STRICT", Title = "Current Claims" },
            new FredSeries { SeriesId = "ICSA", Title = "Initial Claims" }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new FredSeriesRepository(verify)
            .Search("current claims")
            .AsNoTracking()
            .ToListAsync();

        results.Should().ContainSingle().Which.SeriesId.Should().Be("STRICT");
    }
}
