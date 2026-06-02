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
/// Adversarial reconciliation check for GH-749: a 13F-HR/A amendment with a
/// NEW accession, filed for the same (CIK, period) as a previously-imported
/// original, must REPLACE the original — delete-by-period then re-insert —
/// leaving exactly one holding row keyed by
/// (stock, holder, period, shareType, optionType). Anything else means the
/// real-time and bulk paths duplicate or keep stale holdings.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceAmendmentReconciliationTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceAmendmentReconciliationTests(ParadeDbFixture fixture)
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

    private HoldingsImportService CreateImporter(IStockPriceProvider priceProvider) =>
        new(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            priceProvider,
            Substitute.For<MassTransit.IBus>()
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
    public async Task ImportDataSet_AmendmentAfterOriginalSameCikAndPeriod_ReplacesNotDuplicates()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2024, 9, 30);
        var prices = new Dictionary<(Guid, DateOnly), decimal> { [(stock.Id, reportDate)] = 100m };

        const string cover =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n";
        const string infoHeader =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n";

        // 1) Original 13F-HR — 1000 shares.
        using (
            var original = BuildArchive(
                (
                    "SUBMISSION.tsv",
                    "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                        + "13F-HR\tACC-ORIG\t2024-10-15\t2024-09-30\t0001067983\n"
                ),
                ("COVERPAGE.tsv", cover + "ACC-ORIG\tN\tBig Fund\tOmaha\tNE\t028-1\t1\n"),
                (
                    "INFOTABLE.tsv",
                    infoHeader + "ACC-ORIG\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n"
                )
            )
        )
        {
            var sut = CreateImporter(PriceProviderReturning(prices));
            await sut.ImportDataSet(original, new DateOnly(2024, 1, 1), CancellationToken.None);
        }

        // 2) 13F-HR/A amendment — NEW accession, same CIK+period, reduced to 250.
        using (
            var amendment = BuildArchive(
                (
                    "SUBMISSION.tsv",
                    "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                        + "13F-HR/A\tACC-AMEND\t2024-11-20\t2024-09-30\t0001067983\n"
                ),
                ("COVERPAGE.tsv", cover + "ACC-AMEND\tY\tBig Fund\tOmaha\tNE\t028-1\t1\n"),
                (
                    "INFOTABLE.tsv",
                    infoHeader + "ACC-AMEND\t037833100\t250\tSH\t\tSOLE\t250\t0\t0\tCOM\t\n"
                )
            )
        )
        {
            var sut = CreateImporter(PriceProviderReturning(prices));
            await sut.ImportDataSet(amendment, new DateOnly(2024, 1, 1), CancellationToken.None);
        }

        // Reconciliation contract: exactly one row, holding the amended value —
        // not duplicated, not the stale 1000-share original.
        using var verify = FreshContext();
        var holdings = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.CommonStockId == stock.Id)
            .ToListAsync();

        holdings.Should().ContainSingle();
        var holding = holdings[0];
        holding.Shares.Should().Be(250);
        holding.Value.Should().Be(25_000);
        holding.AccessionNumber.Should().Be("ACC-AMEND");
        holding.IsAmendment.Should().BeTrue();
        holding.ReportDate.Should().Be(reportDate);

        // The InstitutionalFiling rollup must track the same reconciliation: the
        // amendment's filing row replaces the original's, with the amended counts.
        // The restated original (ACC-ORIG) must NOT linger as a ghost in the feed.
        var filings = await verify.Set<InstitutionalFiling>().ToListAsync();
        filings.Should().ContainSingle();
        var filing = filings[0];
        filing.AccessionNumber.Should().Be("ACC-AMEND");
        filing.PositionCount.Should().Be(1);
        filing.TotalValue.Should().Be(25_000);
        filing.IsAmendment.Should().BeTrue();
        filing.ReportDate.Should().Be(reportDate);
    }

    [Fact]
    public async Task ImportDataSet_NewHoldingsAmendmentOverwritingAPosition_RecomputesOriginalFilingRow()
    {
        // A "NEW HOLDINGS" amendment does NOT delete the original's holdings, but the
        // holdings upsert key excludes the accession, so re-filing an existing position
        // overwrites that row and flips its accession to the amendment's. The filing
        // rollup must recompute the ORIGINAL filing row too (its position count drops),
        // not leave it stale — even though the original accession is absent from this
        // archive's submissions. This pins the (holder, quarter) recompute unit.
        var aapl = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        var msft = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft Corp",
            Cik = "0000789019",
            Cusip = "594918104",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().AddRange(aapl, msft);
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2024, 9, 30);
        var prices = new Dictionary<(Guid, DateOnly), decimal>
        {
            [(aapl.Id, reportDate)] = 100m,
            [(msft.Id, reportDate)] = 100m,
        };

        const string coverWithType =
            "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\n";
        const string infoHeader =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n";

        // 1) Original 13F-HR — two positions (AAPL 1000, MSFT 500) under ACC-ORIG.
        using (
            var original = BuildArchive(
                (
                    "SUBMISSION.tsv",
                    "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                        + "13F-HR\tACC-ORIG\t2024-10-15\t2024-09-30\t0001067983\n"
                ),
                ("COVERPAGE.tsv", coverWithType + "ACC-ORIG\tN\t\tBig Fund\n"),
                (
                    "INFOTABLE.tsv",
                    infoHeader
                        + "ACC-ORIG\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n"
                        + "ACC-ORIG\t594918104\t500\tSH\t\tSOLE\t500\t0\t0\tCOM\t\n"
                )
            )
        )
        {
            var sut = CreateImporter(PriceProviderReturning(prices));
            await sut.ImportDataSet(original, new DateOnly(2024, 1, 1), CancellationToken.None);
        }

        // 2) NEW HOLDINGS amendment ACC-AMEND — re-files AAPL at 1500 (same key →
        //    overwrites the AAPL row, flipping its accession). MSFT untouched.
        using (
            var amendment = BuildArchive(
                (
                    "SUBMISSION.tsv",
                    "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                        + "13F-HR/A\tACC-AMEND\t2024-11-20\t2024-09-30\t0001067983\n"
                ),
                ("COVERPAGE.tsv", coverWithType + "ACC-AMEND\tY\tNEW HOLDINGS\tBig Fund\n"),
                (
                    "INFOTABLE.tsv",
                    infoHeader + "ACC-AMEND\t037833100\t1500\tSH\t\tSOLE\t1500\t0\t0\tCOM\t\n"
                )
            )
        )
        {
            var sut = CreateImporter(PriceProviderReturning(prices));
            await sut.ImportDataSet(amendment, new DateOnly(2024, 1, 1), CancellationToken.None);
        }

        using var verify = FreshContext();

        // Holdings: AAPL now under ACC-AMEND at 1500; MSFT still under ACC-ORIG at 500.
        var holdings = await verify
            .Set<InstitutionalHolding>()
            .OrderBy(h => h.AccessionNumber)
            .ToListAsync();
        holdings.Should().HaveCount(2);
        holdings.Should().ContainSingle(h => h.AccessionNumber == "ACC-AMEND" && h.Shares == 1500);
        holdings.Should().ContainSingle(h => h.AccessionNumber == "ACC-ORIG" && h.Shares == 500);

        // Filing rollup: the original row is RECOMPUTED to one position (just MSFT),
        // not left stale at two; the amendment row holds the moved AAPL position.
        var filings = await verify
            .Set<InstitutionalFiling>()
            .OrderBy(f => f.AccessionNumber)
            .ToListAsync();
        filings.Should().HaveCount(2);

        var amend = filings.Single(f => f.AccessionNumber == "ACC-AMEND");
        amend.PositionCount.Should().Be(1);
        amend.TotalValue.Should().Be(150_000);
        amend.IsAmendment.Should().BeTrue();

        var orig = filings.Single(f => f.AccessionNumber == "ACC-ORIG");
        orig.PositionCount.Should().Be(1);
        orig.TotalValue.Should().Be(50_000);
        orig.IsAmendment.Should().BeFalse();
    }
}
