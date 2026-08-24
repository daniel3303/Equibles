using System.Net;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class FtdImportServiceInactiveSweepRetryTests
{
    [Fact]
    public async Task BackfillInactiveCusips_TransientFirstFileFailure_DoesNotAdvanceFrontier()
    {
        await using var db = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        db.Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Ticker = "GONE",
                    Name = "Formerly Listed Corp",
                    Cik = "42",
                    Active = false,
                    DelistedOn = new DateOnly(2020, 6, 30),
                    HistoricalCusipBackfillRequestedAt = DateTime.UtcNow.AddMinutes(-1),
                }
            );
        await db.SaveChangesAsync();

        var bus = Substitute.For<IBus>();
        var stockRepo = new CommonStockRepository(db);
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), stockRepo),
            (typeof(CommonStockManager), new CommonStockManager(stockRepo, bus)),
            (typeof(BackfillStateRepository), new BackfillStateRepository(db))
        );
        var secClient = Substitute.For<ISecEdgarClient>();
        secClient
            .DownloadStream(Arg.Any<string>())
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException(
                    "temporary outage",
                    null,
                    HttpStatusCode.ServiceUnavailable
                )
            );
        var sut = new FtdImportService(
            scopeFactory,
            secClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );

        await sut.BackfillInactiveCusips(CancellationToken.None);

        var state = await db.Set<BackfillState>()
            .SingleAsync(row => row.Name == "Ftd.InactiveCusipSweep");
        state.Floor.Should().BeNull();
        await secClient.Received(1).DownloadStream(Arg.Any<string>());
        await bus.DidNotReceiveWithAnyArgs()
            .Publish(default(StockCusipChanged), default(CancellationToken));
    }
}
