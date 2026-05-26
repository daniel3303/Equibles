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
/// Regression for GH-2110. A single 13F-HR can carry many INFOTABLE rows for
/// the same (holder, stock, period, share-type, option-type) tuple — large
/// filers split a position across <c>otherManager</c> codes and the matching
/// rows end up scattered hundreds apart in the filing. Previously
/// <c>StreamAndInsertHoldings</c> flushed every 1000 keys then reset the
/// in-memory aggregator, and <c>FlushBatch</c>'s <c>WhenMatched</c> clause
/// REPLACED the persisted row, so only the last flush's slice survived. The
/// fix flushes at the accession boundary instead, keeping every row of one
/// filing in the same aggregator before any UPSERT runs. This test pins that
/// behaviour by interleaving the two same-key rows with enough other tracked
/// rows that any return to row-count batching would re-trigger the bug.
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

    [Fact]
    public async Task ImportDataSet_SameKeyAcrossBatchBoundary_AccumulatesSharesNotReplaces()
    {
        // Spacing the two AAPL rows with 999 unique padding CUSIPs reproduces
        // the original failure: under the old InsertBatchSize=1000 logic the
        // second AAPL row landed in a fresh batch and the upsert replaced the
        // first batch's 100 shares. The accession-boundary flush keeps both
        // rows aggregated regardless of how far apart they sit.
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
        // AAPL under otherManager 1, 100 shares.
        info.Append("ACC-MULTI\t037833100\t100\tSH\t\tDEFINED\t0\t0\t100\tCOM\t1\n");
        // 999 unique padding rows separating the two AAPL rows.
        foreach (var p in padding)
            info.Append($"ACC-MULTI\t{p.Cusip}\t10\tSH\t\tSOLE\t10\t0\t0\tCOM\t\n");
        // AAPL under otherManager 2, 200 shares. Same upsert key as the
        // first AAPL row but rows apart from it in the INFOTABLE stream.
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
        // 100 + 200 across both otherManager codes. Pre-fix the persisted
        // row was 200 because the second row's batch replaced the first's.
        appleHoldings[0].Shares.Should().Be(300L);
        appleHoldings[0].Value.Should().Be(75_000L);
        appleHoldings[0].VotingAuthNone.Should().Be(300L);
    }
}
