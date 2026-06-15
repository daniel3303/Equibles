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
/// Regression for the missing-BlackRock gap: a Schedule 13D/13G restatement
/// amendment must NOT delete a 13F-HR portfolio that shares the same
/// (holder, report date). BlackRock files monthly 13G/A amendments whose report
/// dates land on quarter ends — exactly the 13F-HR quarter ends — and the old
/// delete-by-(holder, period) wiped the entire 13F portfolio, dropping a ~$5T
/// filer out of the AUM rankings. The delete must be scoped to the amendment's
/// own filing type, so the two forms coexist at the same quarter.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceCrossFilingTypeAmendmentTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceCrossFilingTypeAmendmentTests(ParadeDbFixture fixture)
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
    public async Task ImportDataSet_Schedule13GRestatement_DoesNotDeleteForm13FAtSameReportDate()
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

        // Quarter end shared by the 13F-HR portfolio and the 13G/A stake — the
        // exact collision BlackRock hits every December and March.
        var reportDate = new DateOnly(2024, 9, 30);
        var prices = new Dictionary<(Guid, DateOnly), decimal> { [(stock.Id, reportDate)] = 100m };

        const string cover = "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\n";
        const string infoHeader =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n";

        // 1) The filer's full 13F-HR portfolio for the quarter (AAPL, 1000 shares).
        using (
            var thirteenF = BuildArchive(
                (
                    "SUBMISSION.tsv",
                    "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                        + "13F-HR\tACC-13F\t2024-11-12\t2024-09-30\t0001067983\n"
                ),
                ("COVERPAGE.tsv", cover + "ACC-13F\tN\t\tGiant Asset Manager\n"),
                (
                    "INFOTABLE.tsv",
                    infoHeader + "ACC-13F\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n"
                )
            )
        )
        {
            var sut = CreateImporter(PriceProviderReturning(prices));
            await sut.ImportDataSet(thirteenF, new DateOnly(2020, 1, 1), CancellationToken.None);
        }

        // 2) A Schedule 13G/A RESTATEMENT for the SAME (CIK, period). Pre-fix this
        //    deleted every holding at (holder, 2024-09-30) — including the 13F-HR.
        using (
            var thirteenG = BuildArchive(
                (
                    "SUBMISSION.tsv",
                    "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                        + "SCHEDULE 13G/A\tACC-13G\t2024-11-14\t2024-09-30\t0001067983\n"
                ),
                ("COVERPAGE.tsv", cover + "ACC-13G\tY\t\tGiant Asset Manager\n"),
                (
                    "INFOTABLE.tsv",
                    infoHeader + "ACC-13G\t037833100\t500\tSH\t\tSOLE\t500\t0\t0\tCOM\t\n"
                )
            )
        )
        {
            var sut = CreateImporter(PriceProviderReturning(prices));
            await sut.ImportDataSet(thirteenG, new DateOnly(2020, 1, 1), CancellationToken.None);
        }

        using var verify = FreshContext();
        var holdings = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.CommonStockId == stock.Id && h.ReportDate == reportDate)
            .OrderBy(h => h.FilingType)
            .ToListAsync();

        // Both forms coexist: the 13F-HR portfolio survived the 13G/A restatement.
        holdings.Should().HaveCount(2);

        var form13F = holdings
            .Should()
            .ContainSingle(h => h.FilingType == FilingType.Form13F)
            .Subject;
        form13F.Shares.Should().Be(1000);
        form13F.AccessionNumber.Should().Be("ACC-13F");

        var schedule13G = holdings
            .Should()
            .ContainSingle(h => h.FilingType == FilingType.Schedule13G)
            .Subject;
        schedule13G.Shares.Should().Be(500);
        schedule13G.AccessionNumber.Should().Be("ACC-13G");
    }
}
