using System.Globalization;
using System.Reflection;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// <c>UpdateExistingStock</c> asks EDGAR for a missing website BEFORE it takes the global
/// directory-write advisory lock: a network call must never sit inside a lock every other
/// identity writer waits on. A refill that answers nothing for a row already holding null
/// never takes the lock at all.
/// </summary>
public class CompanySyncServiceWebsiteFetchOutsideLockTests
{
    private const string SourcePath =
        "src/Equibles.Sec.HostedService/Services/CompanySyncService.cs";

    private static long _nextCik = 9_100_000_000;

    private readonly string _cik = Interlocked
        .Increment(ref _nextCik)
        .ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void UpdateExistingStock_FetchesWebsiteBeforeDirectoryWriteLock()
    {
        var source = File.ReadAllText(FindRepositoryPath(SourcePath));
        var start = source.IndexOf("Task UpdateExistingStock(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = source.IndexOf("private ", start + 1, StringComparison.Ordinal);
        var method = source[start..end];

        var fetch = method.IndexOf("FetchWebsite(secCompany.Cik)", StringComparison.Ordinal);
        var lockCall = method.IndexOf("BeginDirectoryIdentityWrite(", StringComparison.Ordinal);
        fetch.Should().BeGreaterThan(0);
        lockCall.Should().BeGreaterThan(fetch);
        method.LastIndexOf("FetchWebsite(", StringComparison.Ordinal).Should().Be(fetch);
    }

    [Fact]
    public async Task NullWebsite_BlankAnswer_NothingElseChanged_DoesNotWrite()
    {
        var db = NewDb();
        using var _ = db;
        db.Set<EquityIssuer>()
            .Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Cik: _cik,
                    Ticker: "EXM",
                    Name: "Example Corp",
                    Website: null
                )
            );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        EquityIssuer stock = await new EquityIssuerRepository(db)
            .GetAll()
            .FirstAsync(s => s.Cik == _cik);
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.GetCompanyMetadata(_cik).Returns(new CompanyMetadata { Website = "" });
        var repository = Substitute.ForPartsOf<EquityIssuerRepository>(db);

        await Invoke(
            BuildSut(edgar),
            new CompanyInfo
            {
                Cik = _cik,
                Name = "Example Corp",
                Tickers = ["EXM"],
            },
            "EXM",
            BuildState(db, stock, repository)
        );

        await edgar.Received(1).GetCompanyMetadata(_cik);
        await repository.DidNotReceive().BeginDirectoryIdentityWrite(Arg.Any<CancellationToken>());
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static CompanySyncService BuildSut(ISecEdgarClient edgarClient) =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            edgarClient,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<IBus>()
        );

    private static object BuildState(
        EquiblesFinancialDbContext db,
        EquityIssuer existingStock,
        EquityIssuerRepository repository
    )
    {
        var t = typeof(CompanySyncService).GetNestedType("StockSyncState", BindingFlags.NonPublic);
        var s = Activator.CreateInstance(t);
        void Set(string n, object v) => t.GetProperty(n).SetValue(s, v);
        Set("SecCiks", new HashSet<string> { existingStock.Cik });
        Set("ExistingStocks", new List<EquityIssuer> { existingStock });
        Set("ExistingCiks", new HashSet<string> { existingStock.Cik });
        Set(
            "ExistingPrimaryTickers",
            new HashSet<string> { existingStock.Presentation.Listing.Ticker }
        );
        Set("PrimaryTickerToStock", new Dictionary<string, EquityIssuer>());
        Set("SecondaryCikToParent", new Dictionary<string, EquityIssuer>());
        Set("CommonStockRepository", repository);
        Set(
            "CommonStockManager",
            new EquityIdentityManager(new EquityIssuerRepository(db), Substitute.For<IBus>())
        );
        Set("DbContext", db);
        return s;
    }

    private static Task Invoke(
        CompanySyncService sut,
        CompanyInfo secCompany,
        string primaryTicker,
        object state
    )
    {
        var m = typeof(CompanySyncService).GetMethod(
            "UpdateExistingStock",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        return (Task)m.Invoke(sut, [secCompany, primaryTicker, new List<string>(), state]);
    }

    private static string FindRepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {relativePath} above {AppContext.BaseDirectory}"
        );
    }
}
