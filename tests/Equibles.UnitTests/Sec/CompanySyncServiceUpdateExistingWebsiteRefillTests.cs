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
/// Pins <c>UpdateExistingStock</c>'s website refill gate. The SEC metadata
/// website field is blank for most companies, so rows that captured that
/// blank hold <c>""</c> — they must count as missing (eligible for a refill)
/// and a blank fetch result must be stored as null, not as a captured
/// empty-string website that permanently blocks every later source.
/// </summary>
public class CompanySyncServiceUpdateExistingWebsiteRefillTests
{
    public CompanySyncServiceUpdateExistingWebsiteRefillTests()
    {
        // The blank-website memo is static process state; earlier tests sharing a
        // CIK would otherwise suppress the refill fetch these tests pin.
        CompanySyncService.ClearBlankWebsiteMemoForTests();
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
            )
        );

    private static object BuildState(EquiblesFinancialDbContext db, CommonStock existingStock)
    {
        var t = typeof(CompanySyncService).GetNestedType("StockSyncState", BindingFlags.NonPublic);
        var s = Activator.CreateInstance(t);
        void Set(string n, object v) => t.GetProperty(n).SetValue(s, v);
        Set("SecCiks", new HashSet<string> { existingStock.Cik });
        Set("ExistingStocks", new List<CommonStock> { existingStock });
        Set("ExistingCiks", new HashSet<string> { existingStock.Cik });
        Set("ExistingPrimaryTickers", new HashSet<string> { existingStock.Ticker });
        Set("PrimaryTickerToStock", new Dictionary<string, CommonStock>());
        Set("SecondaryCikToParent", new Dictionary<string, CommonStock>());
        Set("CommonStockRepository", new CommonStockRepository(db));
        Set(
            "CommonStockManager",
            new CommonStockManager(new CommonStockRepository(db), Substitute.For<IBus>())
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

    private static async Task<(EquiblesFinancialDbContext Db, CommonStock Stock)> SeedStock(
        string website
    )
    {
        var db = NewDb();
        db.Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Cik = "0000000002",
                    Ticker = "EXM",
                    Name = "Example Corp",
                    Website = website,
                }
            );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stock = await db.Set<CommonStock>().FirstAsync(s => s.Cik == "0000000002");
        return (db, stock);
    }

    private static CompanyInfo UnchangedCompanyInfo() =>
        new()
        {
            Cik = "0000000002",
            Name = "Example Corp",
            Tickers = ["EXM"],
        };

    [Fact]
    public async Task EmptyStringWebsite_CountsAsMissing_AndIsRefilled()
    {
        var (db, stock) = await SeedStock("");
        using var _ = db;
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyMetadata("0000000002")
            .Returns(new CompanyMetadata { Website = "https://www.example.com" });

        await Invoke(BuildSut(edgar), UnchangedCompanyInfo(), "EXM", BuildState(db, stock));

        var updated = await db.Set<CommonStock>().FirstAsync(s => s.Cik == "0000000002");
        updated.Website.Should().Be("https://www.example.com");
    }

    [Fact]
    public async Task BlankFetchResult_IsStoredAsNull_NotEmptyString()
    {
        var (db, stock) = await SeedStock("");
        using var _ = db;
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.GetCompanyMetadata("0000000002").Returns(new CompanyMetadata { Website = "" });

        await Invoke(BuildSut(edgar), UnchangedCompanyInfo(), "EXM", BuildState(db, stock));

        var updated = await db.Set<CommonStock>().FirstAsync(s => s.Cik == "0000000002");
        updated.Website.Should().BeNull();
    }

    [Fact]
    public async Task ExistingWebsite_IsNotRefetched()
    {
        var (db, stock) = await SeedStock("https://www.already-set.com");
        using var _ = db;
        var edgar = Substitute.For<ISecEdgarClient>();

        await Invoke(BuildSut(edgar), UnchangedCompanyInfo(), "EXM", BuildState(db, stock));

        await edgar.DidNotReceive().GetCompanyMetadata(Arg.Any<string>());
        var updated = await db.Set<CommonStock>().FirstAsync(s => s.Cik == "0000000002");
        updated.Website.Should().Be("https://www.already-set.com");
    }
}
