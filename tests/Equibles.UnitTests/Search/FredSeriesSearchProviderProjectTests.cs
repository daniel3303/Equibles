using System.Reflection;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories.Search;
using Equibles.Search.Abstractions;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins FredSeriesSearchProvider.Project (new global search #885, 0% covered).
/// Subtitle appends units only when a real unit string exists — a whitespace
/// Units must NOT emit a dangling "SeriesId · " separator (a naive == null check
/// would). Also pins the cross-component wiring HitUrl's "EconomicSeries" arm
/// relies on (Kind + RouteValues["seriesId"]). Project is protected → reflection.
/// </summary>
public class FredSeriesSearchProviderProjectTests
{
    [Fact]
    public void Project_WhitespaceUnits_SubtitleIsSeriesIdOnlyAndWiresEconomicSeriesRoute()
    {
        var provider = new FredSeriesSearchProvider(null);
        var series = new FredSeries
        {
            SeriesId = "GDP",
            Title = "Gross Domestic Product",
            Units = "   ",
        };

        var project = typeof(FredSeriesSearchProvider).GetMethod(
            "Project",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var hit = (SearchHit)project.Invoke(provider, [series])!;

        hit.Subtitle.Should().Be("GDP");
        hit.Kind.Should().Be("EconomicSeries");
        hit.RouteValues.Should().ContainKey("seriesId").WhoseValue.Should().Be("GDP");
        hit.Title.Should().Be("Gross Domestic Product");
    }
}
