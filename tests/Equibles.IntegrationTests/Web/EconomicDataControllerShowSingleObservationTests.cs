using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

public class EconomicDataControllerShowSingleObservationTests
{
    [Fact]
    public async Task Show_SeriesWithExactlyOneObservation_RendersViewInsteadOfThrowing()
    {
        // Contract: a valid series detail page must render. Show casts
        // DescriptiveStatistics.StandardDeviation straight to decimal (no SafeRound
        // guard, unlike MarketController); for one sample StdDev is NaN -> overflow.
        using var ctx = TestDbContextFactory.Create(new FredModuleConfiguration());
        var series = new FredSeries
        {
            SeriesId = "DGS10",
            Title = "10-Year Treasury Constant Maturity Rate",
            Category = FredSeriesCategory.InterestRates,
            Frequency = "Daily",
            Units = "Percent",
            SeasonalAdjustment = "Not Seasonally Adjusted",
        };
        ctx.Add(series);
        ctx.Add(
            new FredObservation
            {
                FredSeriesId = series.Id,
                Date = new DateOnly(2025, 1, 2),
                Value = 4.55m,
            }
        );
        await ctx.SaveChangesAsync();

        var sut = new EconomicDataController(
            new FredSeriesRepository(ctx),
            new FredObservationRepository(ctx),
            Substitute.For<ILogger<EconomicDataController>>()
        );

        var result = await sut.Show("dgs10");

        result
            .Should()
            .BeOfType<ViewResult>(
                "a valid series with a single observation must still render — the "
                    + "StandardDeviation cast must not OverflowException on NaN"
            );
    }
}
