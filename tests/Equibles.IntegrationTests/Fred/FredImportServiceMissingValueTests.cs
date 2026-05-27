using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Fred.Data;
using Equibles.Fred.HostedService.Services;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Integrations.Fred.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// FRED uses the literal "." as its missing-observation sentinel — every real
/// series has gaps marked that way. The existing FredImportService pins only
/// feed numeric values. Contract (stated in the code): a "." observation must
/// still be persisted, with a NULL value, so the series stays date-continuous;
/// dropping the row or zeroing it corrupts the indicator. Untested.
/// </summary>
public class FredImportServiceMissingValueTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public FredImportServiceMissingValueTests()
    {
        _dbContext = TestDbContextFactory.Create(new FredModuleConfiguration());
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task ImportSeries_ObservationWithDotSentinel_PersistedAsNullValueNotDropped()
    {
        var seriesRepo = new FredSeriesRepository(_dbContext);
        var obsRepo = new FredObservationRepository(_dbContext);
        var fredClient = Substitute.For<IFredClient>();
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FredSeriesRepository), seriesRepo),
            (typeof(FredObservationRepository), obsRepo)
        );
        var sut = new FredImportService(
            scopeFactory,
            Substitute.For<ILogger<FredImportService>>(),
            fredClient,
            Options.Create(new WorkerOptions { MinSyncDate = new DateTime(2020, 1, 1) }),
            Substitute.For<ErrorReporter>(
                Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var curated = CuratedSeriesRegistry.Series[0];
        fredClient
            .GetSeriesMetadata(curated.SeriesId)
            .Returns(
                Task.FromResult(
                    new FredSeriesRecord
                    {
                        Id = curated.SeriesId,
                        Title = "Test Series",
                        FrequencyShort = "D",
                        Units = "Percent",
                        SeasonalAdjustmentShort = "NSA",
                        ObservationStart = "2020-01-01",
                        ObservationEnd = "2024-12-31",
                    }
                )
            );
        fredClient
            .GetObservations(curated.SeriesId, Arg.Any<DateOnly?>())
            .Returns(
                Task.FromResult(
                    new List<FredObservationRecord>
                    {
                        new() { Date = "2024-01-01", Value = "5.33" },
                        new() { Date = "2024-01-02", Value = "." },
                    }
                )
            );

        var importSeries = typeof(FredImportService).GetMethod(
            "ImportSeries",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        await (Task)importSeries.Invoke(sut, [curated, CancellationToken.None]);

        var series = await seriesRepo.GetBySeriesId(curated.SeriesId).FirstOrDefaultAsync();
        series.Should().NotBeNull();
        var obs = obsRepo.GetBySeries(series!).ToList();

        obs.Should().HaveCount(2, "the missing-value row must be kept, not dropped");
        obs.Should().Contain(o => o.Date == new DateOnly(2024, 1, 1) && o.Value == 5.33m);
        obs.Should()
            .Contain(
                o => o.Date == new DateOnly(2024, 1, 2) && o.Value == null,
                "a '.' sentinel must persist as a null value, keeping the series date-continuous"
            );
    }
}
