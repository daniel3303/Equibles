using System.Reflection;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// A holding-company reorganization moves a ticker to a new CIK that filed a successor-issuer
/// report. The sync continues the existing issuer under that CIK with the predecessor CIK
/// attached, instead of retiring the issuer and creating an empty one, and an issuer whose own
/// listing was withdrawn no longer counts as holding its ticker.
/// </summary>
public class CompanySyncServiceSuccessorAdoptionTests
{
    private const string PredecessorCik = "1593538";
    private const string SuccessorCik = "2152623";
    private const string Ticker = "NAVI";
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
        db.Database.EnsureCreated();
        return db;
    }

    private static (CompanySyncService Sut, ISecEdgarClient Edgar, CapturingLogger Log) BuildSut()
    {
        var edgar = Substitute.For<ISecEdgarClient>();
        var log = new CapturingLogger();
        var sut = new CompanySyncService(
            Substitute.For<IServiceScopeFactory>(),
            edgar,
            Options.Create(new WorkerOptions()),
            log,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<IBus>()
        );
        return (sut, edgar, log);
    }

    // The sync's per-cycle state as BuildSyncState shapes it: feed-scoped stocks and CIKs, and the
    // active primary tickers over every row.
    private static object BuildState(
        EquiblesFinancialDbContext db,
        IReadOnlyList<EquityIssuer> stocks,
        params string[] secCiks
    )
    {
        var type = typeof(CompanySyncService).GetNestedType(
            "StockSyncState",
            BindingFlags.NonPublic
        );
        var state = Activator.CreateInstance(type);
        void Set(string name, object value) => type.GetProperty(name).SetValue(state, value);
        var feed = secCiks.Select(CikNormalizer.Canonicalize).ToHashSet(StringComparer.Ordinal);
        var inFeed = stocks
            .Where(stock => feed.Contains(CikNormalizer.Canonicalize(stock.Cik)))
            .ToList();
        var active = stocks
            .Where(stock =>
                stock.Presentation?.Listing is { MarketCountryCode: "US", Active: true }
            )
            .ToList();
        Set("SecCiks", feed);
        Set("ExistingStocks", inFeed);
        Set(
            "ExistingCiks",
            inFeed
                .Select(stock => CikNormalizer.Canonicalize(stock.Cik))
                .ToHashSet(StringComparer.Ordinal)
        );
        Set(
            "ExistingPrimaryTickers",
            active
                .Select(stock => stock.Presentation.Listing.Ticker)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        );
        Set(
            "PrimaryTickerToStock",
            active.ToDictionary(
                stock => stock.Presentation.Listing.Ticker,
                stock => stock,
                StringComparer.OrdinalIgnoreCase
            )
        );
        Set("SecondaryCikToParent", new Dictionary<string, EquityIssuer>());
        Set("CommonStockRepository", new EquityIssuerRepository(db));
        Set(
            "CommonStockManager",
            new EquityIdentityManager(new EquityIssuerRepository(db), Substitute.For<IBus>())
        );
        Set("DbContext", db);
        return state;
    }

    private static T Get<T>(object state, string name) =>
        (T)state.GetType().GetProperty(name).GetValue(state);

    private static Task Invoke(
        CompanySyncService sut,
        string method,
        CompanyInfo company,
        object state
    ) =>
        (Task)
            typeof(CompanySyncService)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(sut, [company, Ticker, new List<string>(), state]);

    // The ticker ledger lists the issuer's CIK under the ticker until heldUntil (by default the previous
    // capture), as it does for a registrant SEC's directory just moved the ticker away from.
    private static async Task<EquityIssuer> Seed(
        EquiblesFinancialDbContext db,
        string cik,
        string name,
        bool active,
        DateOnly? heldUntil = null
    )
    {
        var seed = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: cik,
            Ticker: Ticker,
            Name: name,
            Active: active,
            Website: "https://www.example.com",
            SecondaryTickers: active ? ["JSM"] : null
        );
        seed.SecRegistrantProfileReadOn = Today;
        var lastObservedOn = heldUntil ?? Today.AddDays(-1);
        db.Add(seed);
        db.Add(
            new SecTickerDirectoryObservation
            {
                Cik = cik,
                Ticker = Ticker,
                Exchange = "Nasdaq",
                FirstObservedOn = lastObservedOn.AddDays(-30),
                LastObservedOn = lastObservedOn,
            }
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return await new EquityIssuerRepository(db).GetAll().FirstAsync(stock => stock.Cik == cik);
    }

    private static CompanyMetadata Successor(SuccessorRegistration registration) =>
        new()
        {
            Cik = SuccessorCik,
            FilerCik = SuccessorCik,
            Name = "Navient Holdings, Inc.",
            EntityType = "other",
            SuccessorRegistration = registration,
        };

    private static SuccessorRegistration Report(DateOnly filedOn) =>
        new("8-K12B", filedOn, "0001193125-26-436899");

    private static CompanyInfo SuccessorCompany() =>
        new()
        {
            Cik = SuccessorCik,
            Name = "Navient Holdings, Inc.",
            Tickers = [Ticker],
        };

    private static async Task AssertContinuedUnderTheSuccessor(
        EquiblesFinancialDbContext db,
        EquityIssuer incumbent,
        object state
    )
    {
        var issuers = await new EquityIssuerRepository(db).GetAll().ToListAsync();
        var issuer = issuers.Should().ContainSingle().Subject;
        issuer.Id.Should().Be(incumbent.Id);
        issuer.Cik.Should().Be(SuccessorCik);
        issuer.SecondaryCiks.Should().Equal(PredecessorCik);
        issuer.Name.Should().Be(CompanyNameCasing.Normalize("Navient Holdings, Inc."));
        issuer.NameAliases.Should().Contain("Navient Corp");
        issuer.EntityType.Should().Be("other");
        issuer
            .SecRegistrantProfileReadOn.Should()
            .BeNull("the profile is re-read under the new CIK");
        issuer.Presentation.Listing.Ticker.Should().Be(Ticker);
        issuer.Presentation.Listing.Active.Should().BeTrue();
        issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "JSM")
            .IsDirectoryListed.Should()
            .BeFalse("the successor's directory entry lists only the common stock");

        Get<Dictionary<string, EquityIssuer>>(state, "SecondaryCikToParent")
            .Should()
            .ContainKey(PredecessorCik)
            .WhoseValue.Id.Should()
            .Be(incumbent.Id);
        Get<HashSet<string>>(state, "ExistingCiks").Should().Contain(SuccessorCik);
        Get<Dictionary<string, EquityIssuer>>(state, "PrimaryTickerToStock")[Ticker]
            .Id.Should()
            .Be(incumbent.Id);
    }

    [Fact]
    public async Task ARecentSuccessorReport_ContinuesTheIncumbentUnderTheNewCik()
    {
        using var db = NewDb();
        var incumbent = await Seed(db, PredecessorCik, "Navient Corp", active: true);
        var (sut, edgar, log) = BuildSut();
        edgar.GetCompanyMetadata(SuccessorCik).Returns(Successor(Report(Today.AddDays(-3))));
        var state = BuildState(db, [incumbent], SuccessorCik);

        await Invoke(sut, "ReplaceObsoleteStock", SuccessorCompany(), state);

        log.Errors.Should().BeEmpty();
        await AssertContinuedUnderTheSuccessor(db, incumbent, state);
    }

    [Fact]
    public async Task ASuccessorReport_WithBothCiksInTheDirectory_StillContinuesTheIncumbent()
    {
        using var db = NewDb();
        var incumbent = await Seed(db, PredecessorCik, "Navient Corp", active: true);
        var (sut, edgar, log) = BuildSut();
        edgar.GetCompanyMetadata(SuccessorCik).Returns(Successor(Report(Today.AddDays(-3))));
        var state = BuildState(db, [incumbent], PredecessorCik, SuccessorCik);

        await Invoke(sut, "ReplaceObsoleteStock", SuccessorCompany(), state);

        log.Errors.Should().BeEmpty();
        log.Warnings.Should().BeEmpty("succession is not a parent/subsidiary collision");
        await AssertContinuedUnderTheSuccessor(db, incumbent, state);
    }

    // "lapsed": the incumbent's directory entry ended months ago, so a successor report by the new
    // holder of its ticker speaks of another company's reorganization.
    [Theory]
    [InlineData("none")]
    [InlineData("stale")]
    [InlineData("lapsed")]
    public async Task WithoutACurrentSuccessorReport_TheIncumbentIsRetiredAsBefore(string report)
    {
        using var db = NewDb();
        var incumbent = await Seed(
            db,
            PredecessorCik,
            "Navient Corp",
            active: true,
            heldUntil: report == "lapsed" ? Today.AddDays(-90) : null
        );
        var (sut, edgar, _) = BuildSut();
        edgar
            .GetCompanyMetadata(SuccessorCik)
            .Returns(
                Successor(
                    report switch
                    {
                        "stale" => Report(Today.AddDays(-200)),
                        "lapsed" => Report(Today.AddDays(-3)),
                        _ => null,
                    }
                )
            );
        var state = BuildState(db, [incumbent], SuccessorCik);

        await Invoke(sut, "ReplaceObsoleteStock", SuccessorCompany(), state);

        var issuers = await new EquityIssuerRepository(db).GetAll().ToListAsync();
        issuers.Should().HaveCount(2);
        var retired = issuers.Single(issuer => issuer.Id == incumbent.Id);
        retired.Cik.Should().Be(PredecessorCik);
        retired.Presentation.Listing.Active.Should().BeFalse();
        var created = issuers.Single(issuer => issuer.Id != incumbent.Id);
        created.Cik.Should().Be(SuccessorCik);
        created.Presentation.Listing.Ticker.Should().Be(Ticker);
        created.Presentation.Listing.Active.Should().BeTrue();
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("http")]
    public async Task AnUnreadableEdgarIndex_KeepsTheIncumbentForTheNextCycle(string failure)
    {
        using var db = NewDb();
        var incumbent = await Seed(db, PredecessorCik, "Navient Corp", active: true);
        var (sut, edgar, log) = BuildSut();
        edgar
            .GetCompanyMetadata(SuccessorCik)
            .Returns(
                failure == "http"
                    ? Task.FromException<CompanyMetadata>(new HttpRequestException("429"))
                    : Task.FromResult<CompanyMetadata>(null)
            );
        var state = BuildState(db, [incumbent], SuccessorCik);

        await Invoke(sut, "ReplaceObsoleteStock", SuccessorCompany(), state);

        var issuer = (await new EquityIssuerRepository(db).GetAll().ToListAsync())
            .Should()
            .ContainSingle()
            .Subject;
        issuer.Cik.Should().Be(PredecessorCik);
        issuer
            .Presentation.Listing.Active.Should()
            .BeTrue("nothing is retired on an unknown answer");
        log.Warnings.Should().ContainSingle(warning => warning.Contains("until the next cycle"));
        log.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task AWithdrawnListing_DoesNotHoldItsTicker_SoTheUpdateSkipsWithAWarning()
    {
        using var db = NewDb();
        var predecessor = await Seed(db, PredecessorCik, "Navient Corp", active: false);
        var successor = await Seed(db, SuccessorCik, "Navient Holdings, Inc.", active: true);
        var (sut, _, log) = BuildSut();
        var state = BuildState(db, [predecessor, successor], PredecessorCik, SuccessorCik);

        await Invoke(
            sut,
            "UpdateExistingStock",
            new CompanyInfo
            {
                Cik = PredecessorCik,
                Name = "NAVIENT CORP",
                Tickers = [Ticker],
            },
            state
        );

        log.Errors.Should()
            .BeEmpty("the ticker belongs to the active issuer, which is a skip, not a failure");
        log.Warnings.Should().ContainSingle(warning => warning.Contains("already in use"));
        var issuers = await new EquityIssuerRepository(db).GetAll().ToListAsync();
        issuers
            .Single(issuer => issuer.Id == predecessor.Id)
            .Presentation.Listing.Active.Should()
            .BeFalse();
        var holder = issuers.Single(issuer => issuer.Id == successor.Id);
        holder.Presentation.Listing.Ticker.Should().Be(Ticker);
        holder.Presentation.Listing.Active.Should().BeTrue();
    }

    private sealed class CapturingLogger : ILogger<CompanySyncService>
    {
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
