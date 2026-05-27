using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the contract from GH-1591: when a CommonStock row is removed between
/// BuildTickerMap and FlushBatch, FtdImportService must persist rows for the
/// surviving stocks instead of letting one stale CommonStockId fail the whole
/// UpsertRange with FK_FailToDeliver_CommonStock_CommonStockId. Without the
/// guard, Postgres rolls back the entire batch and no FailToDeliver row lands
/// for any ticker in the same flush.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FtdImportServiceMissingCommonStockTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FtdImportServiceMissingCommonStockTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

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

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(
                            new CommonStockRepository(ctx),
                            Substitute.For<IBus>()
                        )
                    );
                sp.GetService(typeof(FailToDeliverRepository))
                    .Returns(new FailToDeliverRepository(ctx));
                sp.GetService(typeof(TickerMapService)).Returns(new TickerMapService(scopeFactory));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    [Fact]
    public async Task Import_CommonStockDeletedBeforeFlush_PersistsRowsForSurvivingStocks()
    {
        // Two stocks in the same FTD batch. AAPL will be removed mid-import to
        // simulate the GH-1591 race; MSFT must survive. Pre-fix: Postgres
        // rejects the whole UpsertRange with FK_FailToDeliver_CommonStock,
        // rolling MSFT back too. Post-fix: AAPL is filtered out and MSFT lands.
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        var msft = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
            Cusip = "594918104",
        };

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().AddRange(apple, msft);
            await seed.SaveChangesAsync();
        }

        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1).AddDays(-1);
        var csv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{settlementDate:yyyyMMdd}|037833100|AAPL|12345|APPLE INC|187.50\n"
            + $"{settlementDate:yyyyMMdd}|594918104|MSFT|54321|MICROSOFT CORP|420.00\n";

        var deletedOnce = false;
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns(_ =>
            {
                // Race: CompanySync removes AAPL after BuildTickerMap and before FlushBatch.
                // We hook DownloadStream because it's the only side-effecting touchpoint that
                // sits between those two points in the FTD import flow.
                if (!deletedOnce)
                {
                    using var deleteCtx = _fixture.CreateDbContext();
                    var staleApple = deleteCtx.Set<CommonStock>().Single(s => s.Ticker == "AAPL");
                    deleteCtx.Set<CommonStock>().Remove(staleApple);
                    deleteCtx.SaveChanges();
                    deletedOnce = true;
                }
                return Task.FromResult<Stream>(BuildFtdZipStream(csv));
            });

        var sut = new FtdImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = settlementDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }
            )
        );

        await sut.Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();

        var msftRow = await verify
            .Set<FailToDeliver>()
            .SingleOrDefaultAsync(f =>
                f.CommonStockId == msft.Id && f.SettlementDate == settlementDate
            );
        msftRow
            .Should()
            .NotBeNull(
                "the surviving MSFT row must be persisted even when AAPL's parent row vanished "
                    + "between BuildTickerMap and FlushBatch; pre-fix the FK violation on AAPL "
                    + "rolls the whole UpsertRange back and MSFT is dropped too"
            );
        msftRow!.Quantity.Should().Be(54321);

        var appleRow = await verify
            .Set<FailToDeliver>()
            .SingleOrDefaultAsync(f => f.CommonStockId == apple.Id);
        appleRow
            .Should()
            .BeNull("the deleted AAPL parent must not get an orphan FailToDeliver row");
    }

    private static Stream BuildFtdZipStream(string csvBody)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("cnsfails.txt");
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(csvBody);
            stream.Write(bytes, 0, bytes.Length);
        }
        buffer.Position = 0;
        return buffer;
    }
}
