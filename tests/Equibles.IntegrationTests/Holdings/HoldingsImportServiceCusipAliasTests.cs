using System.Globalization;
using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// After an issuer-level CUSIP change, filings referencing the retired CUSIP
/// must keep resolving to the stock: laggard 13F filers use it for a quarter or
/// two, and every historical data set uses it forever — including the full
/// re-import that a CUSIP change itself triggers (StockCusipChangedConsumer
/// clears the processed-data-set ledger). BBUC made the failure visible: its
/// Class A conversion retired 11259V106 for 113006100, and until aliases
/// existed, whichever CUSIP was NOT stored on the stock silently dropped at
/// BuildCusipMapping. Pin both resolution rules: (1) a retired CUSIP maps via
/// its <see cref="CommonStockCusipAlias"/> row, (2) a stock's CURRENT CUSIP
/// wins over another stock's alias claiming the same CUSIP.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceCusipAliasTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceCusipAliasTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
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
                sp.GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(ctx));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private HoldingsImportService CreateImporter(IStockPriceProvider priceProvider)
    {
        return new HoldingsImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            priceProvider,
            Substitute.For<MassTransit.IBus>()
        );
    }

    private static ZipArchive BuildArchive(params (string Name, string Body)[] entries)
    {
        var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                var entry = writer.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(body);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static IStockPriceProvider PriceProviderReturning(
        Dictionary<(Guid, DateOnly), decimal> prices
    )
    {
        var provider = Substitute.For<IStockPriceProvider>();
        provider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(prices));
        return provider;
    }

    [Fact]
    public async Task ImportDataSet_FilingReferencesRetiredCusip_ResolvesViaAliasToStock()
    {
        // BBUC post-change shape: the stock carries the NEW CUSIP; a laggard
        // filer (or any historical data set) still reports the OLD one.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BBUC",
            Name = "Brookfield Business Corp",
            Cik = "1654795",
            Cusip = "113006100",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<CommonStockCusipAlias>()
                .Add(new CommonStockCusipAlias { CommonStockId = stock.Id, Cusip = "11259V106" });
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2026, 3, 31);
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-001\t2026-05-08\t2026-03-31\t0001142031\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-001\tN\tPrivate Management Group\tIrvine\tCA\t028-04556\t105909\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-001\t11259V106\t921231\tSH\t\tSOLE\t921231\t0\t0\tCL A EXC SUB VTG\t\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var prices = new Dictionary<(Guid, DateOnly), decimal> { [(stock.Id, reportDate)] = 32m };
        var sut = CreateImporter(PriceProviderReturning(prices));

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2026, 1, 1),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();

        using var verify = FreshContext();
        var holding = await verify.Set<InstitutionalHolding>().SingleAsync();
        holding.CommonStockId.Should().Be(stock.Id);
        holding.Cusip.Should().Be("11259V106");
        holding.Shares.Should().Be(921231);
    }

    [Fact]
    public async Task ImportDataSet_AliasCollidesWithAnotherStocksCurrentCusip_CurrentCusipWins()
    {
        // Precedence pin: if a CUSIP is simultaneously stock A's CURRENT value
        // and stock B's retired alias (a shape only bad data can produce), the
        // current assignment is authoritative.
        var stockA = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAA",
            Name = "Current Owner Corp",
            Cik = "0000000001",
            Cusip = "999999999",
        };
        var stockB = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BBB",
            Name = "Stale Alias Corp",
            Cik = "0000000002",
            Cusip = "888888888",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().AddRange(stockA, stockB);
            seed.Set<CommonStockCusipAlias>()
                .Add(new CommonStockCusipAlias { CommonStockId = stockB.Id, Cusip = "999999999" });
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2026, 3, 31);
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-002\t2026-05-08\t2026-03-31\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-002\tN\tBerkshire Hathaway\tOmaha\tNE\t028-12345\t12345\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-002\t999999999\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var prices = new Dictionary<(Guid, DateOnly), decimal>
        {
            [(stockA.Id, reportDate)] = 100m,
            [(stockB.Id, reportDate)] = 100m,
        };
        var sut = CreateImporter(PriceProviderReturning(prices));

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2026, 1, 1),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();

        using var verify = FreshContext();
        var holding = await verify.Set<InstitutionalHolding>().SingleAsync();
        holding.CommonStockId.Should().Be(stockA.Id);
    }
}
