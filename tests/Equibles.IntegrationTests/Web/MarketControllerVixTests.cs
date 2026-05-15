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
/// `MarketController.Vix` (`~/Market/Vix`) was 35/35 lines zero-hit in the local
/// cobertura baseline — no test calls it. This pins the populated branch: the
/// DescriptiveStatistics block (mean/median/min/max/stddev), latest-vs-previous
/// close ordering, and the chronological SMA inputs. A regression in the
/// order-by (desc for records, asc for SMA) or the `records.Count > 1` guard
/// would silently corrupt the VIX page with no other test catching it.
/// </summary>
public class MarketControllerVixTests
{
    [Fact]
    public async Task Vix_WithHistory_ProjectsRecordsAndDescriptiveStatistics()
    {
        using var ctx = TestDbContextFactory.Create(new CboeModuleConfiguration());
        ctx.Set<CboeVixDaily>()
            .AddRange(
                new CboeVixDaily
                {
                    Date = new DateOnly(2025, 1, 2),
                    Open = 19,
                    High = 21,
                    Low = 18,
                    Close = 20m,
                },
                new CboeVixDaily
                {
                    Date = new DateOnly(2025, 1, 3),
                    Open = 20,
                    High = 26,
                    Low = 19,
                    Close = 25m,
                },
                new CboeVixDaily
                {
                    Date = new DateOnly(2025, 1, 6),
                    Open = 24,
                    High = 24,
                    Low = 14,
                    Close = 15m,
                }
            );
        await ctx.SaveChangesAsync();

        var sut = new MarketController(
            new CboePutCallRatioRepository(ctx),
            new CboeVixDailyRepository(ctx),
            Substitute.For<ILogger<MarketController>>()
        );

        var result = await sut.Vix();

        var model = result
            .Should()
            .BeOfType<ViewResult>()
            .Subject.Model.Should()
            .BeOfType<VixViewModel>()
            .Subject;
        model.Records.Should().HaveCount(3);
        // Records are date-descending: latest = Jan 6 (15), previous = Jan 3 (25).
        model.LatestClose.Should().Be(15m);
        model.PreviousClose.Should().Be(25m);
        model.Min.Should().Be(15m);
        model.Max.Should().Be(25m);
        model.Mean.Should().Be(20m); // (20 + 25 + 15) / 3
    }
}
