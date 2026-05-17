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
using Equibles.Messaging.Contracts.CommonStocks;
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
/// PR #839 deliberately routed FTD CUSIP seeding through CommonStockManager so
/// a StockCusipChanged event is published (outbox) — that's the trigger for the
/// Holdings module to backfill 13F data sets processed while the stock had no
/// CUSIP. The FullPipeline pin only asserts the CUSIP was lifted and uses an
/// unretained publish substitute, so a regression reverting to a direct
/// `stock.Cusip = cusip` assignment would pass it yet silently kill the
/// backfill. Pin: SeedCusips publishes StockCusipChanged for the resolved stock.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FtdImportServiceSeedCusipsPublishesEventTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public FtdImportServiceSeedCusipsPublishesEventTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    [Fact]
    public async Task SeedCusips_ResolvesCusipOntoCusiplessStock_PublishesStockCusipChanged()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(apple);
            await seed.SaveChangesAsync();
        }

        var publishEndpoint = Substitute.For<IPublishEndpoint>();
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
                        new CommonStockManager(new CommonStockRepository(ctx), publishEndpoint)
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var sut = new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );

        // Build the internal FtdRecord list + tickerMap via reflection (FtdRecord
        // is internal; SeedCusips is private).
        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Cusip")!.SetValue(record, "037833100");
        recordType.GetProperty("Symbol")!.SetValue(record, "AAPL");
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        list.Add(record);
        var tickerMap = new Dictionary<string, Guid> { ["AAPL"] = apple.Id };

        var seedCusips = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var seeded = await (Task<int>)
            seedCusips.Invoke(sut, [list, tickerMap, CancellationToken.None])!;

        seeded.Should().Be(1);
        await publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e =>
                    e.CommonStockId == apple.Id
                    && e.Ticker == "AAPL"
                    && e.Cusip == "037833100"
                    && e.PreviousCusip == null
                ),
                Arg.Any<CancellationToken>()
            );

        using var verify = FreshContext();
        var persisted = await verify.Set<CommonStock>().FirstAsync(s => s.Id == apple.Id);
        persisted.Cusip.Should().Be("037833100");
    }
}
