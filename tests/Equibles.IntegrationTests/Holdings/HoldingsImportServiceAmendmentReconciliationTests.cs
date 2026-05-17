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
    private readonly List<EquiblesDbContext> _contexts = [];
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
    }
}
