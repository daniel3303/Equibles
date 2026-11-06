using System.Net;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Canada;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// A publisher can move a release, so each read replaces its upcoming dates; a past date stays, and a
/// source that fails or changed shape must leave what is stored, since an empty calendar reads as
/// "nothing scheduled".
/// </summary>
public class CanadianReleaseCalendarImportServiceTests : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FredSeriesRepository _seriesRepo;
    private readonly FredReleaseRepository _releaseRepo;
    private readonly FredReleaseDateRepository _dateRepo;
    private readonly IHttpClientFactory _httpFactory = Substitute.For<IHttpClientFactory>();
    private readonly CanadianReleaseCalendarImportService _sut;
    private string _statCan = "[]";
    private string _bank = "";

    public CanadianReleaseCalendarImportServiceTests()
    {
        _dbContext = TestDbContextFactory.Create(new FredModuleConfiguration());
        _seriesRepo = new FredSeriesRepository(_dbContext);
        _releaseRepo = new FredReleaseRepository(_dbContext);
        _dateRepo = new FredReleaseDateRepository(_dbContext);
        _httpFactory
            .CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(
                new AddressHandler(uri =>
                    uri.Host.Contains("statcan", StringComparison.Ordinal) ? _statCan : _bank
                )
            ));
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FredSeriesRepository), _seriesRepo),
            (typeof(FredReleaseRepository), _releaseRepo),
            (typeof(FredReleaseDateRepository), _dateRepo)
        );
        _sut = new CanadianReleaseCalendarImportService(
            scopeFactory,
            _httpFactory,
            Substitute.For<ILogger<CanadianReleaseCalendarImportService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Import_StoresCanadianReleasesAndLinksTheirSeries()
    {
        var series = new FredSeries
        {
            SeriesId = "STATCAN-V41690973",
            Title = "Consumer Price Index: All Items for Canada",
            CountryCode = "CA",
            Source = IndicatorSource.StatisticsCanada,
        };
        _dbContext.Add(series);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        var date = Today.AddDays(10);
        _statCan = Schedule(("Consumer Price Index", date, "October 2026"));

        await _sut.Import(CancellationToken.None);

        var release = await _releaseRepo.GetAll().SingleAsync(CancellationToken.None);
        release
            .Should()
            .Match<FredRelease>(r =>
                r.CountryCode == "CA"
                && r.Source == IndicatorSource.StatisticsCanada
                && r.ReleaseId == null
                && r.Importance == FredReleaseImportance.High
            );
        var stored = await _dateRepo.GetAll().SingleAsync(CancellationToken.None);
        stored.Date.Should().Be(date);
        stored.ReferencePeriod.Should().Be("October 2026");
        series.FredReleaseId.Should().Be(release.Id);
    }

    [Fact]
    public async Task Import_AMovedDateReplacesTheUpcomingOneAndAPastDateStays()
    {
        var past = Today.AddDays(-20);
        var first = new DateOnly(Today.Year + 1, 3, 10);
        _statCan = Schedule(("Retail trade", first, "September 2026"));
        await _sut.Import(CancellationToken.None);
        var release = await _releaseRepo.GetAll().SingleAsync(CancellationToken.None);
        _dateRepo.Add(new FredReleaseDate { FredReleaseId = release.Id, Date = past });
        await _dateRepo.SaveChanges();

        var moved = first.AddDays(1);
        _statCan = Schedule(("Retail trade", moved, "September 2026"));
        await _sut.Import(CancellationToken.None);

        var dates = await _dateRepo
            .GetAll()
            .OrderBy(d => d.Date)
            .Select(d => d.Date)
            .ToListAsync(CancellationToken.None);
        dates.Should().Equal(past, moved);
    }

    [Fact]
    public async Task Import_AScheduleThatChangedShapeKeepsTheStoredDates()
    {
        var date = Today.AddDays(5);
        _statCan = Schedule(("Retail trade", date, "September 2026"));
        await _sut.Import(CancellationToken.None);

        _statCan = "{\"moved\":true}";
        await _sut.Import(CancellationToken.None);

        var stored = await _dateRepo.GetAll().SingleAsync(CancellationToken.None);
        stored.Date.Should().Be(date);
    }

    // A page that lost one year's table parses cleanly, so the import itself keeps what that year had stored.
    [Fact]
    public async Task Import_ASourceThatLostAYearKeepsThatYearsDates()
    {
        var nearer = new DateOnly(Today.Year + 1, 6, 1);
        var further = new DateOnly(Today.Year + 2, 6, 1);
        _statCan = Schedule(("Retail trade", nearer, "April"), ("Retail trade", further, "April"));
        await _sut.Import(CancellationToken.None);

        _statCan = Schedule(("Retail trade", nearer, "April"));
        await _sut.Import(CancellationToken.None);

        var dates = await _dateRepo
            .GetAll()
            .OrderBy(d => d.Date)
            .Select(d => d.Date)
            .ToListAsync(CancellationToken.None);
        dates.Should().Equal(nearer, further);
    }

    private static string Schedule(params (string Title, DateOnly Date, string Period)[] rows) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(
            rows.Select(row => new
            {
                date = $"{row.Date:yyyy-MM-dd} 00:00:01",
                type = "meeting",
                title = row.Title,
                description = row.Period,
                url = "",
            })
        );

    private sealed class AddressHandler(Func<Uri, string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body(request.RequestUri!)),
                }
            );
    }
}
