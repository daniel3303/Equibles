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

public class MarketControllerTests {
    [Fact]
    public async Task PutCallRatio_UnknownTypeString_ReturnsNotFound() {
        // The route accepts {type} as a raw string and converts to CboePutCallRatioType
        // via Enum.TryParse. A regression that drops the TryParse guard would either
        // throw or fall through to a 200 with empty data — both are worse than 404
        // for an unknown ratio type.
        using var ctx = TestDbContextFactory.Create(new CboeModuleConfiguration());
        var putCallRepo = new CboePutCallRatioRepository(ctx);
        var vixRepo = new CboeVixDailyRepository(ctx);
        var sut = new MarketController(putCallRepo, vixRepo, Substitute.For<ILogger<MarketController>>());

        var result = await sut.PutCallRatio("not-a-real-ratio-type");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task PutCallRatio_SingleRecord_RendersViewWithNullStdDev() {
        // DescriptiveStatistics.StandardDeviation returns NaN for a single-value sample
        // and casting NaN to decimal throws OverflowException. The action must guard the
        // cast and surface a null StdDev rather than 500-ing.
        using var ctx = TestDbContextFactory.Create(new CboeModuleConfiguration());
        ctx.Set<CboePutCallRatio>().Add(new CboePutCallRatio {
            RatioType = CboePutCallRatioType.Equity,
            Date = new DateOnly(2025, 1, 2),
            CallVolume = 1_000_000,
            PutVolume = 750_000,
            TotalVolume = 1_750_000,
            PutCallRatio = 0.75m
        });
        await ctx.SaveChangesAsync();

        var putCallRepo = new CboePutCallRatioRepository(ctx);
        var vixRepo = new CboeVixDailyRepository(ctx);
        var sut = new MarketController(putCallRepo, vixRepo, Substitute.For<ILogger<MarketController>>());

        var result = await sut.PutCallRatio("equity");

        var view = result.Should().BeOfType<ViewResult>().Subject;
        var model = view.Model.Should().BeOfType<PutCallRatioViewModel>().Subject;
        model.Records.Should().HaveCount(1);
        model.LatestRatio.Should().Be(0.75m);
        model.Mean.Should().Be(0.75m);
        model.Median.Should().Be(0.75m);
        model.Min.Should().Be(0.75m);
        model.Max.Should().Be(0.75m);
        model.StdDev.Should().BeNull("StandardDeviation is undefined for a single-value sample");
    }
}
