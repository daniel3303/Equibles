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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using MassTransit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins <c>UpdateExistingStock</c>'s obsolete-holder-removal arm (Branch A) —
/// reachable only when the ticker an existing CIK now wants is held by a stock
/// whose own CIK dropped out of the SEC feed. Driven directly via reflection
/// with a fully-constructed <c>StockSyncState</c> so there is no
/// orchestration role-mapping ambiguity (the path the prior orchestration
/// attempts couldn't reach deterministically).
/// </summary>
public class CompanySyncServiceUpdateExistingBranchATests
{
    private static EquiblesDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesDbContext(options, [new CommonStocksModuleConfiguration()]);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static CompanySyncService BuildSut() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ISecEdgarClient>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

    private static object BuildState(
        EquiblesDbContext db,
        CommonStock existingStock,
        CommonStock tickerHolder,
        string primaryTicker
    )
    {
        var t = typeof(CompanySyncService).GetNestedType("StockSyncState", BindingFlags.NonPublic);
        var s = Activator.CreateInstance(t);
        void Set(string n, object v) => t.GetProperty(n).SetValue(s, v);
        // existingStock is in the feed; tickerHolder's CIK is NOT in SecCiks.
        Set("SecCiks", new HashSet<string> { existingStock.Cik });
        Set("ExistingStocks", new List<CommonStock> { existingStock });
        Set("ExistingCiks", new HashSet<string> { existingStock.Cik });
        Set("ExistingPrimaryTickers", new HashSet<string> { existingStock.Ticker, primaryTicker });
        Set("ExistingSecondaryTickers", new HashSet<string>());
        Set(
            "PrimaryTickerToStock",
            new Dictionary<string, CommonStock> { [primaryTicker] = tickerHolder }
        );
        Set("SecondaryCikToParent", new Dictionary<string, CommonStock>());
        Set("CommonStockRepository", new CommonStockRepository(db));
        Set("CommonStockManager", new CommonStockManager(new CommonStockRepository(db), Substitute.For<IPublishEndpoint>()));
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

    [Fact]
    public async Task UpdateExistingStock_TickerHeldByOutOfFeedStock_DeletesObsoleteHolderThenUpdates()
    {
        using var db = NewDb();
        var existing = new CommonStock
        {
            Cik = "0000000002",
            Ticker = "OLD",
            Name = "Old Name",
        };
        var obsolete = new CommonStock
        {
            Cik = "0000000999",
            Ticker = "NEWT",
            Name = "Obsolete Holder",
        };
        db.Set<CommonStock>().AddRange(existing, obsolete);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Re-read the tracked instances the state will hand to the service.
        var existingTracked = await db.Set<CommonStock>().FirstAsync(s => s.Cik == "0000000002");
        var obsoleteTracked = await db.Set<CommonStock>().FirstAsync(s => s.Cik == "0000000999");
        var state = BuildState(db, existingTracked, obsoleteTracked, "NEWT");
        var secCompany = new CompanyInfo
        {
            Cik = "0000000002",
            Name = "Updated Name",
            Tickers = ["NEWT"],
        };

        await Invoke(BuildSut(), secCompany, "NEWT", state);

        (await db.Set<CommonStock>().AnyAsync(s => s.Cik == "0000000999"))
            .Should()
            .BeFalse("the obsolete holder must have been deleted");
        var updated = await db.Set<CommonStock>().FirstAsync(s => s.Cik == "0000000002");
        updated.Ticker.Should().Be("NEWT");
        updated.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateExistingStock_ObsoleteDeleteFails_LogsReportsAndReturns()
    {
        var db = NewDb();
        var existing = new CommonStock
        {
            Cik = "0000000002",
            Ticker = "OLD",
            Name = "Old Name",
        };
        var obsolete = new CommonStock
        {
            Cik = "0000000999",
            Ticker = "NEWT",
            Name = "Obsolete Holder",
        };
        db.Set<CommonStock>().AddRange(existing, obsolete);
        await db.SaveChangesAsync();
        var state = BuildState(db, existing, obsolete, "NEWT");
        var secCompany = new CompanyInfo
        {
            Cik = "0000000002",
            Name = "Updated Name",
            Tickers = ["NEWT"],
        };

        // Dispose the context so the obsolete-holder SaveChanges throws,
        // exercising Branch A's catch (log + report + early return).
        db.Dispose();

        await Invoke(BuildSut(), secCompany, "NEWT", state);

        existing.Ticker.Should().Be("OLD", "Branch A returned before the main update ran");
    }
}
