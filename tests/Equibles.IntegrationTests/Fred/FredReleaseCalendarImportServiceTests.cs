using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Services;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Integrations.Fred.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// The release-calendar importer has two jobs: link every stored series to its
/// FRED release (creating the release once, even when several series share it)
/// and persist the scheduled/realized dates of tracked releases only. Pin both,
/// plus the dedup against already-stored dates — re-inserting would violate the
/// (release, date) unique index on every cycle after the first.
/// </summary>
public class FredReleaseCalendarImportServiceTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FredSeriesRepository _seriesRepo;
    private readonly FredReleaseRepository _releaseRepo;
    private readonly FredReleaseDateRepository _dateRepo;
    private readonly IFredClient _fredClient;
    private readonly FredReleaseCalendarImportService _sut;

    public FredReleaseCalendarImportServiceTests()
    {
        _dbContext = TestDbContextFactory.Create(new FredModuleConfiguration());
        _seriesRepo = new FredSeriesRepository(_dbContext);
        _releaseRepo = new FredReleaseRepository(_dbContext);
        _dateRepo = new FredReleaseDateRepository(_dbContext);
        _fredClient = Substitute.For<IFredClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FredSeriesRepository), _seriesRepo),
            (typeof(FredReleaseRepository), _releaseRepo),
            (typeof(FredReleaseDateRepository), _dateRepo)
        );
        _sut = new FredReleaseCalendarImportService(
            scopeFactory,
            Substitute.For<ILogger<FredReleaseCalendarImportService>>(),
            _fredClient,
            Substitute.For<ErrorReporter>(
                Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Import_TwoSeriesSharingARelease_CreatesOneReleaseAndKeepsOnlyTrackedDates()
    {
        _seriesRepo.Add(new FredSeries { SeriesId = "CPIAUCSL", Title = "CPI All Items" });
        _seriesRepo.Add(new FredSeries { SeriesId = "CPILFESL", Title = "CPI Core" });
        await _seriesRepo.SaveChanges();

        var cpiRelease = new FredReleaseRecord
        {
            Id = 10,
            Name = "Consumer Price Index",
            PressRelease = true,
            Link = "http://www.bls.gov/cpi/",
        };
        _fredClient.GetSeriesRelease("CPIAUCSL").Returns(Task.FromResult(cpiRelease));
        _fredClient.GetSeriesRelease("CPILFESL").Returns(Task.FromResult(cpiRelease));
        _fredClient
            .GetReleaseDates(Arg.Any<DateOnly?>())
            .Returns(
                Task.FromResult(
                    new List<FredReleaseDateRecord>
                    {
                        new()
                        {
                            ReleaseId = 10,
                            ReleaseName = "Consumer Price Index",
                            Date = "2026-06-11",
                        },
                        new()
                        {
                            ReleaseId = 10,
                            ReleaseName = "Consumer Price Index",
                            Date = "2026-07-14",
                        },
                        // Untracked release — no series of ours links to it.
                        new()
                        {
                            ReleaseId = 99,
                            ReleaseName = "Some Other Release",
                            Date = "2026-06-12",
                        },
                        // Malformed date — must be skipped, not crash the cycle.
                        new()
                        {
                            ReleaseId = 10,
                            ReleaseName = "Consumer Price Index",
                            Date = "not-a-date",
                        },
                    }
                )
            );

        await _sut.Import(CancellationToken.None);

        var releases = _releaseRepo.GetAll().ToList();
        releases.Should().HaveCount(1, "both series share FRED release 10");
        releases[0].ReleaseId.Should().Be(10);
        releases[0].Name.Should().Be("Consumer Price Index");
        releases[0].PressRelease.Should().BeTrue();

        var series = _seriesRepo.GetAll().ToList();
        series.Should().OnlyContain(s => s.FredReleaseId == releases[0].Id);

        var dates = _dateRepo.GetAll().ToList();
        dates.Should().HaveCount(2, "only tracked releases' parseable dates are stored");
        dates
            .Select(d => d.Date)
            .Should()
            .BeEquivalentTo([new DateOnly(2026, 6, 11), new DateOnly(2026, 7, 14)]);
        dates.Should().OnlyContain(d => d.FredReleaseId == releases[0].Id);
    }

    [Fact]
    public async Task Import_DateAlreadyStored_InsertsOnlyTheNewDate()
    {
        var release = new FredRelease { ReleaseId = 10, Name = "Consumer Price Index" };
        _releaseRepo.Add(release);
        await _releaseRepo.SaveChanges();
        _seriesRepo.Add(
            new FredSeries
            {
                SeriesId = "CPIAUCSL",
                Title = "CPI All Items",
                FredReleaseId = release.Id,
            }
        );
        await _seriesRepo.SaveChanges();
        _dateRepo.Add(new FredReleaseDate { FredReleaseId = release.Id, Date = new(2026, 6, 11) });
        await _dateRepo.SaveChanges();

        _fredClient
            .GetReleaseDates(Arg.Any<DateOnly?>())
            .Returns(
                Task.FromResult(
                    new List<FredReleaseDateRecord>
                    {
                        new()
                        {
                            ReleaseId = 10,
                            ReleaseName = "Consumer Price Index",
                            Date = "2026-06-11",
                        },
                        new()
                        {
                            ReleaseId = 10,
                            ReleaseName = "Consumer Price Index",
                            Date = "2026-07-14",
                        },
                    }
                )
            );

        await _sut.Import(CancellationToken.None);

        // Every series is already linked, so no series/release lookups happen.
        await _fredClient.DidNotReceive().GetSeriesRelease(Arg.Any<string>());

        var dates = _dateRepo.GetAll().ToList();
        dates.Should().HaveCount(2, "the already-stored date must not be duplicated");
        dates
            .Select(d => d.Date)
            .Should()
            .BeEquivalentTo([new DateOnly(2026, 6, 11), new DateOnly(2026, 7, 14)]);
    }
}
