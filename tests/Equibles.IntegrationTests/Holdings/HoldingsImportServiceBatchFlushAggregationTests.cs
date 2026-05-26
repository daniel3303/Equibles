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
/// GH-2110 — when a single 13F-HR carries multiple INFOTABLE rows for the same
/// (holder, stock, period, share-type, option-type) tuple (typical for large
/// filers that split a position across <c>otherManager</c> codes) and those
/// rows are scattered far enough apart in the file to span the in-memory flush
/// boundary (<c>StreamAndInsertHoldings</c>'s <c>InsertBatchSize = 1000</c>
/// unique keys), only the LAST batch's slice survives.
///
/// In-batch aggregation in <c>holdingsMap</c> is correct, but the
/// <c>FlushBatch</c> upsert's <c>WhenMatched</c> clause overwrites the
/// persisted row rather than accumulating, so a key that already flushed in an
/// earlier batch gets its prior sum silently replaced.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceBatchFlushAggregationTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceBatchFlushAggregationTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
        return Task.CompletedTask;
    }

    private EquiblesDbContext FreshContext()
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
                sp.GetService(typeof(EquiblesDbContext)).Returns(ctx);
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

    private HoldingsImportService CreateImporter(IStockPriceProvider priceProvider) =>
        new(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            priceProvider
        );

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

    [Fact(
        Skip = "GH-2110 — cross-batch entries for the same upsert key get replaced instead of accumulated"
    )]
    public async Task ImportDataSet_SameKeyAcrossBatchBoundary_AccumulatesSharesNotReplaces()
    {
        // The flush triggers at 1000 unique keys in holdingsMap. To force the
        // two AAPL rows into different batches: row 1 = AAPL (key #1), rows
        // 2..1000 = 999 unique padding CUSIPs (keys #2..#1000) which trips the
        // flush after row 1000, row 1001 = AAPL again — now in a fresh batch.
        const int paddingCount = 999;

        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        var padding = Enumerable
            .Range(0, paddingCount)
            .Select(i => new CommonStock
            {
                Id = Guid.NewGuid(),
                Ticker = $"PAD{i:D4}",
                Name = $"Padding {i}",
                Cik = (1000000 + i).ToString("D10", CultureInfo.InvariantCulture),
                Cusip = $"PAD{i:D6}",
            })
            .ToList();

        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(apple);
            seed.Set<CommonStock>().AddRange(padding);
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2025, 12, 31);
        var prices = new Dictionary<(Guid, DateOnly), decimal> { [(apple.Id, reportDate)] = 250m };
        foreach (var p in padding)
            prices[(p.Id, reportDate)] = 1m;

        const string coverHeader =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n";
        const string infoHeader =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n";

        var info = new StringBuilder(infoHeader);
        // AAPL #1 — under otherManager 1, 100 shares.
        info.Append("ACC-MULTI\t037833100\t100\tSH\t\tDEFINED\t0\t0\t100\tCOM\t1\n");
        // 999 unique padding rows to fill out batch 1.
        foreach (var p in padding)
            info.Append($"ACC-MULTI\t{p.Cusip}\t10\tSH\t\tSOLE\t10\t0\t0\tCOM\t\n");
        // AAPL #2 — under otherManager 2, 200 shares. Same upsert key as AAPL
        // #1, but lands in batch 2 after the boundary flush + map clear.
        info.Append("ACC-MULTI\t037833100\t200\tSH\t\tDEFINED\t0\t0\t200\tCOM\t2\n");

        using var archive = BuildArchive(
            (
                "SUBMISSION.tsv",
                "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                    + "13F-HR\tACC-MULTI\t2026-01-29\t2025-12-31\t0000102909\n"
            ),
            (
                "COVERPAGE.tsv",
                coverHeader + "ACC-MULTI\tN\tVanguard Group\tMalvern\tPA\t028-06408\t\n"
            ),
            ("INFOTABLE.tsv", info.ToString())
        );

        var sut = CreateImporter(PriceProviderReturning(prices));
        await sut.ImportDataSet(archive, new DateOnly(2025, 1, 1), CancellationToken.None);

        using var verify = FreshContext();
        var appleHoldings = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.CommonStockId == apple.Id)
            .ToListAsync();

        // Both AAPL rows share one upsert key — exactly one persisted row.
        appleHoldings.Should().ContainSingle();
        // Bug: only batch 2's 200 shares survives. Expected: 100 + 200 = 300.
        appleHoldings[0].Shares.Should().Be(300L);
        appleHoldings[0].Value.Should().Be(75_000L);
        appleHoldings[0].VotingAuthNone.Should().Be(300L);
    }
}
