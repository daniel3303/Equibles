using System.Collections;
using System.Reflection;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The SEC fails-to-deliver feed strips the class-share separator from symbols
/// (Berkshire Hathaway B is "BRKB", Moog A is "MOGA") while EDGAR — and therefore
/// CommonStock.Ticker — keeps it ("BRK-B", "MOG-A"). CUSIP seeding must bridge the
/// two forms, otherwise class-share issuers never receive a CUSIP, stay hidden from
/// the public portal, and none of their 13F holdings can map to the company
/// (daniel3303/EquiblesCommercial#2508). Pin: a feed symbol that is the
/// separator-stripped form of exactly one stored ticker seeds that stock's CUSIP.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FtdImportServiceSeedCusipsClassShareSymbolTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FtdImportServiceSeedCusipsClassShareSymbolTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    [Fact]
    public async Task SeedCusips_FeedSymbolWithoutClassSeparator_SeedsCusipOnHyphenatedTicker()
    {
        var berkshire = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BRK-B",
            Name = "Berkshire Hathaway Inc",
            Cik = "0001067983",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(berkshire);
            await seed.SaveChangesAsync();
        }

        var sut = BuildService();

        // The real cnsfails feed line for Berkshire B carries symbol "BRKB".
        var (records, recordType) = BuildRecords(("BRKB", "084670702"));
        var tickerMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            ["BRK-B"] = berkshire.Id,
        };

        var seeded = await InvokeSeedCusips(sut, records, tickerMap);

        seeded.Should().Be(1);
        using var verify = FreshContext();
        var persisted = await verify.Set<CommonStock>().FirstAsync(s => s.Id == berkshire.Id);
        persisted.Cusip.Should().Be("084670702");
    }

    [Fact]
    public async Task SeedCusips_StrippedSymbolCollidesWithRealTicker_SeedsOnlyTheExactMatch()
    {
        // "BFA" is both a real stored ticker AND the stripped form of "BF-A".
        // The exact owner of the symbol must win; the class-share stock must
        // NOT be seeded from an ambiguous symbol — guessing would attach the
        // wrong CUSIP to the wrong company.
        var exact = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BFA",
            Name = "Exact Symbol Owner Inc",
            Cik = "0000000010",
        };
        var classShare = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BF-A",
            Name = "Brown Forman Corp",
            Cik = "0000014693",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().AddRange(exact, classShare);
            await seed.SaveChangesAsync();
        }

        var sut = BuildService();

        var (records, _) = BuildRecords(("BFA", "111111111"));
        var tickerMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            ["BFA"] = exact.Id,
            ["BF-A"] = classShare.Id,
        };

        var seeded = await InvokeSeedCusips(sut, records, tickerMap);

        seeded.Should().Be(1);
        using var verify = FreshContext();
        var exactAfter = await verify.Set<CommonStock>().FirstAsync(s => s.Id == exact.Id);
        var classAfter = await verify.Set<CommonStock>().FirstAsync(s => s.Id == classShare.Id);
        exactAfter.Cusip.Should().Be("111111111");
        classAfter.Cusip.Should().BeNull();
    }

    private FtdImportService BuildService()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(
                            new CommonStockRepository(ctx),
                            Substitute.For<IBus>()
                        )
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        return new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );
    }

    private static (IList Records, Type RecordType) BuildRecords(
        params (string Symbol, string Cusip)[] rows
    )
    {
        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        foreach (var (symbol, cusip) in rows)
        {
            var record = Activator.CreateInstance(recordType)!;
            recordType.GetProperty("Symbol")!.SetValue(record, symbol);
            recordType.GetProperty("Cusip")!.SetValue(record, cusip);
            list.Add(record);
        }
        return (list, recordType);
    }

    private static async Task<int> InvokeSeedCusips(
        FtdImportService sut,
        IList records,
        Dictionary<string, Guid> tickerMap
    )
    {
        var seedCusips = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        return await (Task<int>)
            seedCusips.Invoke(sut, [records, tickerMap, CancellationToken.None])!;
    }
}
