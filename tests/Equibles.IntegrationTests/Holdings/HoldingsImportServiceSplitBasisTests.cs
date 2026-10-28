using System.Globalization;
using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.Data.Models;
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
/// Against Postgres, through the whole data-set import: Bank of Nova Scotia's Broadcom Q2 2024
/// count, filed after the 10:1 split on the later basis, is rebased onto the report date from its
/// stored prior quarter once the quarter's earlier filers confirm the price basis, and a replay of
/// the same data set keeps one evidence row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceSplitBasisTests : IAsyncLifetime
{
    private static readonly DateOnly PriorQuarter = new(2024, 3, 31);
    private static readonly DateOnly ReportDate = new(2024, 6, 30);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceSplitBasisTests(ParadeDbFixture fixture)
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

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private HoldingsImportService CreateImporter(Guid issuerId)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(ctx));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        // Stored closes on today's basis.
        var prices = new Dictionary<(Guid, string, DateOnly), decimal>
        {
            [(issuerId, null, PriorQuarter)] = 132.541m,
            [(issuerId, null, ReportDate)] = 160.553m,
        };
        var priceProvider = Substitute.For<IStockPriceProvider>();
        priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(prices));
        return new HoldingsImportService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            priceProvider,
            Substitute.For<MassTransit.IBus>()
        );
    }

    private static ZipArchive Archive(
        string period,
        params (string Accession, string FilingDate, string Cik, long Shares, long Value)[] filings
    )
    {
        var submission = new StringBuilder(
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
        );
        var coverPage = new StringBuilder(
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
        );
        var infoTable = new StringBuilder(
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVALUE\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
        );
        foreach (var (accession, filingDate, cik, shares, value) in filings)
        {
            submission.Append($"13F-HR\t{accession}\t{filingDate}\t{period}\t{cik}\n");
            coverPage.Append($"{accession}\tN\tFiler {cik}\tToronto\tA6\t028-{cik[^5..]}\t\n");
            infoTable.Append(
                $"{accession}\t11135F101\t{shares}\tSH\t\tSOLE\t{value}\t{shares}\t0\t0\tCOM\t\n"
            );
        }

        var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (
                var (name, body) in new[]
                {
                    ("SUBMISSION.tsv", submission.ToString()),
                    ("COVERPAGE.tsv", coverPage.ToString()),
                    ("INFOTABLE.tsv", infoTable.ToString()),
                }
            )
            {
                using var stream = writer.CreateEntry(name).Open();
                var bytes = Encoding.UTF8.GetBytes(body);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private async Task<EquityIssuer> SeedBroadcom()
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AVGO",
            Name: "Broadcom Inc.",
            Cik: "1730168",
            Cusip: "11135F101"
        );
        using var seed = FreshContext();
        seed.Set<EquityIssuer>().Add(stock);
        await seed.SaveChangesAsync();
        seed.Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    EquityIssuerId = stock.Id,
                    EquityListingId = stock.Presentation.EquityListingId,
                    PriceSeriesTicker = "AVGO",
                    EffectiveDate = new DateOnly(2024, 7, 15),
                    Numerator = 10m,
                    Denominator = 1m,
                    Source = StockSplitSource.External,
                    PriceAdjustmentAppliedTime = new DateTime(
                        2024,
                        7,
                        16,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc
                    ),
                    ShareCountEvidence = StockSplitShareCountEvidence.Confirmed,
                }
            );
        await seed.SaveChangesAsync();
        return stock;
    }

    // Three holders filed the quarter before the split took effect, the last on the later basis.
    private static ZipArchive SecondQuarter() =>
        Archive(
            "2024-06-30",
            ("0001000001-24-000001", "2024-07-10", "0001000001", 50_000, 80_276_500),
            ("0001000002-24-000001", "2024-07-11", "0001000002", 40_000, 64_221_200),
            ("0001000003-24-000001", "2024-07-12", "0001000003", 30_000, 48_165_900),
            ("0001085146-24-003837", "2024-08-14", "0000009631", 5_420_597, 868_975_493)
        );

    [Fact]
    public async Task ImportDataSet_CountFiledOnTheLaterBasis_IsRebasedFromTheStoredPriorQuarter()
    {
        var stock = await SeedBroadcom();
        var importer = CreateImporter(stock.Id);
        using (
            var first = Archive(
                "2024-03-31",
                ("0001085146-24-002485", "2024-05-15", "0000009631", 504_523, 668_700_389)
            )
        )
            (await importer.ImportDataSet(first, new DateOnly(2020, 1, 1), CancellationToken.None))
                .IsComplete.Should()
                .BeTrue();

        using (var second = SecondQuarter())
            (await importer.ImportDataSet(second, new DateOnly(2020, 1, 1), CancellationToken.None))
                .IsComplete.Should()
                .BeTrue();

        var rebased = await FreshContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .SingleAsync(h => h.AccessionNumber == "0001085146-24-003837");
        rebased.Shares.Should().Be(542_060);
        rebased.VotingAuthSole.Should().Be(542_060);
        rebased.FiledValue.Should().Be(868_975_493);
        rebased.Value.Should().Be(870_293_591);
        rebased.ValueSource.Should().Be(ValueSource.Derived);
        var early = await FreshContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .Where(h => h.ReportDate == ReportDate && h.AccessionNumber != rebased.AccessionNumber)
            .Select(h => h.Shares)
            .ToListAsync();
        early.Should().BeEquivalentTo([50_000L, 40_000L, 30_000L]);

        using (var replay = SecondQuarter())
            (await importer.ImportDataSet(replay, new DateOnly(2020, 1, 1), CancellationToken.None))
                .IsComplete.Should()
                .BeTrue();

        (
            await FreshContext()
                .Set<InstitutionalHolding>()
                .AsNoTracking()
                .SingleAsync(h => h.AccessionNumber == "0001085146-24-003837")
        )
            .Shares.Should()
            .Be(542_060);
        var evidence = await FreshContext()
            .Set<HoldingsCorrectionEvidence>()
            .AsNoTracking()
            .ToListAsync();
        evidence
            .Select(e => (e.CorrectionKey, e.SourceRecordId))
            .Should()
            .Equal(
                (
                    SplitBasisCorrectionEvidence.SharesCorrectionKey,
                    "0001085146-24-003837|11135F101|"
                )
            );
    }
}
