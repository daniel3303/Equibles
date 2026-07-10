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
        releases[0]
            .Importance.Should()
            .Be(FredReleaseImportance.High, "CPI is a curated tier-1 release");

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
    public async Task Import_ContinuousCarryForwardRelease_DropsItButKeepsGenuinePeriodicRelease()
    {
        // The FOMC Press Release is driven by the DFEDTARL/DFEDTARU target range,
        // a daily 7-day rate level whose value is carried forward every calendar
        // day. With include_release_dates_with_no_data=true, FRED fills a release
        // date for EVERY day — including weekends — producing a phantom daily
        // "FOMC Press Release". A genuine periodic release (CPI) prints on real
        // distinct scheduled dates and never on a weekend.
        _seriesRepo.Add(new FredSeries { SeriesId = "DFEDTARU", Title = "Fed Funds Upper" });
        _seriesRepo.Add(new FredSeries { SeriesId = "CPIAUCSL", Title = "CPI All Items" });
        await _seriesRepo.SaveChanges();

        var fomcRelease = new FredReleaseRecord
        {
            Id = 101,
            Name = "FOMC Press Release",
            PressRelease = true,
        };
        var cpiRelease = new FredReleaseRecord
        {
            Id = 10,
            Name = "Consumer Price Index",
            PressRelease = true,
        };
        _fredClient.GetSeriesRelease("DFEDTARU").Returns(Task.FromResult(fomcRelease));
        _fredClient.GetSeriesRelease("CPIAUCSL").Returns(Task.FromResult(cpiRelease));
        _fredClient
            .GetReleaseDates(Arg.Any<DateOnly?>())
            .Returns(
                Task.FromResult(
                    new List<FredReleaseDateRecord>
                    {
                        // FOMC carried forward every day, incl. a Sat (20th) and Sun (21st).
                        new() { ReleaseId = 101, Date = "2026-06-19" }, // Fri
                        new() { ReleaseId = 101, Date = "2026-06-20" }, // Sat
                        new() { ReleaseId = 101, Date = "2026-06-21" }, // Sun
                        new() { ReleaseId = 101, Date = "2026-06-22" }, // Mon
                        // Genuine CPI print on one real weekday date.
                        new() { ReleaseId = 10, Date = "2026-06-11" },
                    }
                )
            );

        await _sut.Import(CancellationToken.None);

        var fomc = _releaseRepo.GetAll().Single(r => r.ReleaseId == 101);
        var cpi = _releaseRepo.GetAll().Single(r => r.ReleaseId == 10);

        var dates = _dateRepo.GetAll().ToList();
        dates
            .Should()
            .OnlyContain(
                d => d.FredReleaseId == cpi.Id,
                "the carried-forward FOMC press release must not appear on the calendar at all"
            );
        dates
            .Select(d => d.Date)
            .Should()
            .BeEquivalentTo(
                [new DateOnly(2026, 6, 11)],
                "only the genuine periodic CPI date survives"
            );
        dates
            .Should()
            .NotContain(d => d.FredReleaseId == fomc.Id, "no FOMC phantom on weekdays either");
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

    [Fact]
    public async Task Import_StoredPhantomDatesOfCarryForwardRelease_ArePurged()
    {
        // Rows persisted BEFORE a release was recognized as carry-forward must not
        // linger: the import is insert-only, so without an explicit purge the phantom
        // daily "FOMC Press Release" rows stored by earlier cycles stay on the
        // calendar forever even though new ones are skipped.
        var fomc = new FredRelease { ReleaseId = 101, Name = "FOMC Press Release" };
        var cpi = new FredRelease { ReleaseId = 10, Name = "Consumer Price Index" };
        _releaseRepo.Add(fomc);
        _releaseRepo.Add(cpi);
        await _releaseRepo.SaveChanges();
        _seriesRepo.Add(
            new FredSeries
            {
                SeriesId = "DFEDTARU",
                Title = "Fed Funds Upper",
                FredReleaseId = fomc.Id,
            }
        );
        _seriesRepo.Add(
            new FredSeries
            {
                SeriesId = "CPIAUCSL",
                Title = "CPI All Items",
                FredReleaseId = cpi.Id,
            }
        );
        await _seriesRepo.SaveChanges();
        _dateRepo.Add(new FredReleaseDate { FredReleaseId = fomc.Id, Date = new(2026, 6, 1) });
        _dateRepo.Add(new FredReleaseDate { FredReleaseId = fomc.Id, Date = new(2026, 6, 2) });
        _dateRepo.Add(new FredReleaseDate { FredReleaseId = cpi.Id, Date = new(2026, 6, 11) });
        await _dateRepo.SaveChanges();

        _fredClient
            .GetReleaseDates(Arg.Any<DateOnly?>())
            .Returns(
                Task.FromResult(
                    new List<FredReleaseDateRecord>
                    {
                        new() { ReleaseId = 101, Date = "2026-06-19" }, // Fri
                        new() { ReleaseId = 101, Date = "2026-06-20" }, // Sat — carry-forward tell
                        new() { ReleaseId = 10, Date = "2026-07-14" },
                    }
                )
            );

        await _sut.Import(CancellationToken.None);

        var dates = _dateRepo.GetAll().ToList();
        dates
            .Should()
            .OnlyContain(
                d => d.FredReleaseId == cpi.Id,
                "every stored FOMC phantom row must be purged, not just new ones skipped"
            );
        dates
            .Select(d => d.Date)
            .Should()
            .BeEquivalentTo([new DateOnly(2026, 6, 11), new DateOnly(2026, 7, 14)]);
    }

    [Fact]
    public async Task Import_ReleaseStoredWithStaleImportance_IsRestampedFromTheCuratedMap()
    {
        // Releases created before the curated importance map existed (or before their
        // entry changed) carry a stale tier; the importer re-stamps every cycle so the
        // map is the single source of truth.
        var cpi = new FredRelease
        {
            ReleaseId = 10,
            Name = "Consumer Price Index",
            Importance = FredReleaseImportance.Low,
        };
        _releaseRepo.Add(cpi);
        await _releaseRepo.SaveChanges();
        _seriesRepo.Add(
            new FredSeries
            {
                SeriesId = "CPIAUCSL",
                Title = "CPI All Items",
                FredReleaseId = cpi.Id,
            }
        );
        await _seriesRepo.SaveChanges();

        _fredClient
            .GetReleaseDates(Arg.Any<DateOnly?>())
            .Returns(Task.FromResult(new List<FredReleaseDateRecord>()));

        await _sut.Import(CancellationToken.None);

        _releaseRepo
            .GetAll()
            .Single(r => r.ReleaseId == 10)
            .Importance.Should()
            .Be(FredReleaseImportance.High);
    }

    [Fact]
    public async Task Import_EveryTrackedReleaseCarriedForward_PersistsNoDatesAndDoesNotThrow()
    {
        // When every tracked release is a weekend-flagged carry-forward, the drop
        // empties the parsed list. The importer must return cleanly with no calendar
        // rows — and crucially without throwing: the date-range computation right
        // after the drop calls Min/Max, which throw on an empty sequence.
        _seriesRepo.Add(new FredSeries { SeriesId = "DFEDTARU", Title = "Fed Funds Upper" });
        await _seriesRepo.SaveChanges();

        var fomcRelease = new FredReleaseRecord
        {
            Id = 101,
            Name = "FOMC Press Release",
            PressRelease = true,
        };
        _fredClient.GetSeriesRelease("DFEDTARU").Returns(Task.FromResult(fomcRelease));
        _fredClient
            .GetReleaseDates(Arg.Any<DateOnly?>())
            .Returns(
                Task.FromResult(
                    new List<FredReleaseDateRecord>
                    {
                        new() { ReleaseId = 101, Date = "2026-06-19" }, // Fri
                        new() { ReleaseId = 101, Date = "2026-06-20" }, // Sat
                        new() { ReleaseId = 101, Date = "2026-06-22" }, // Mon
                    }
                )
            );

        var import = async () => await _sut.Import(CancellationToken.None);

        await import.Should().NotThrowAsync();
        _dateRepo
            .GetAll()
            .ToList()
            .Should()
            .BeEmpty("a release seen on any weekend is a carry-forward and contributes no dates");
    }
}
