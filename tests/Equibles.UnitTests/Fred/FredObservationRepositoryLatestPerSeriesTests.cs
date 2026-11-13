using Equibles.Data;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Equibles.UnitTests.Fred;

public class FredObservationRepositoryLatestPerSeriesTests
{
    private static readonly InMemoryDatabaseRoot Root = new();

    [Fact]
    public async Task LatestAndPrevious_SkipProjectedObservationsDatedAfterToday()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), Root)
            .Options;
        await using var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new FredModuleConfiguration() }
        );
        var series = new FredSeries { SeriesId = "GDPPOT", Title = "Real Potential GDP" };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        context.Add(series);
        context.AddRange(
            new FredObservation
            {
                FredSeries = series,
                Date = today.AddMonths(-6),
                Value = 1m,
            },
            new FredObservation
            {
                FredSeries = series,
                Date = today.AddMonths(-3),
                Value = 2m,
            },
            new FredObservation
            {
                FredSeries = series,
                Date = today.AddYears(10),
                Value = 9m,
            }
        );
        await context.SaveChangesAsync();
        var repository = new FredObservationRepository(context);

        (await repository.GetLatestPerSeries().SingleAsync()).Value.Should().Be(2m);
        (await repository.GetPreviousPerSeries().SingleAsync()).Value.Should().Be(1m);
    }
}
