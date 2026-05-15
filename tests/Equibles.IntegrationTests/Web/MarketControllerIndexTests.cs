using Equibles.Cboe.Data;
using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Market;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// MarketControllerTests pins only the two PutCallRatio paths; the `~/Market`
/// landing action <c>Index</c> — the whole put/call-summary projection plus the
/// VIX latest/previous/52-week aggregation — was entirely uncovered. This pins
/// it end-to-end against seeded data: a regression in the enum→summary
/// projection, the latest-vs-previous VIX ordering, or the 52-week
/// Max/Min-over-range would render a wrong or empty market dashboard with no
/// other test catching it.
/// </summary>
public class MarketControllerIndexTests
{
    [Fact]
    public async Task Index_WithPutCallAndVixHistory_ProjectsSummariesAndVixAggregates()
    {
        using var ctx = TestDbContextFactory.Create(new CboeModuleConfiguration());
        ctx.Set<CboePutCallRatio>()
            .Add(
                new CboePutCallRatio
                {
                    RatioType = CboePutCallRatioType.Equity,
                    Date = new DateOnly(2025, 1, 3),
                    CallVolume = 1_000_000,
                    PutVolume = 800_000,
                    TotalVolume = 1_800_000,
                    PutCallRatio = 0.8m,
                }
            );
        // Two recent VIX rows: latest = Jan 3 (close 18), previous = Jan 2 (close 22).
        // Both inside the 52-week window, so High=22 / Low=18.
        var recent = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        ctx.Set<CboeVixDaily>()
            .AddRange(
                new CboeVixDaily
                {
                    Date = recent,
                    Open = 20,
                    High = 23,
                    Low = 17,
                    Close = 18m,
                },
                new CboeVixDaily
                {
                    Date = recent.AddDays(-1),
                    Open = 21,
                    High = 24,
                    Low = 19,
                    Close = 22m,
                }
            );
        await ctx.SaveChangesAsync();

        var sut = new MarketController(
            new CboePutCallRatioRepository(ctx),
            new CboeVixDailyRepository(ctx),
            Substitute.For<ILogger<MarketController>>()
        );

        var result = await sut.Index();

        var model = result
            .Should()
            .BeOfType<ViewResult>()
            .Subject.Model.Should()
            .BeOfType<MarketIndexViewModel>()
            .Subject;
        // One summary per ratio enum value; the seeded Equity one carries data.
        model.PutCallRatios.Should().HaveCount(Enum.GetValues<CboePutCallRatioType>().Length);
        model
            .PutCallRatios.Should()
            .ContainSingle(s => s.Type == CboePutCallRatioType.Equity && s.LatestRatio == 0.8m);
        model.Vix.LatestClose.Should().Be(18m);
        model.Vix.PreviousClose.Should().Be(22m);
        model.Vix.High52Week.Should().Be(22m);
        model.Vix.Low52Week.Should().Be(18m);
    }
}
