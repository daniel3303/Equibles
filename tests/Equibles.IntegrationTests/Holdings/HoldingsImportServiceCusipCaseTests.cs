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
/// Filers spell CUSIPs in any case while the identity directory stores them uppercase, and the
/// SQL lookup compares case-sensitively. Pin: a lowercase filed CUSIP resolves to its stock, and
/// replaying the filing over its own retained rows, stored with the filed spelling, updates them
/// in place instead of leaving the filing incomplete.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceCusipCaseTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceCusipCaseTests(ParadeDbFixture fixture)
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
        return scopeFactory;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportDataSet_LowercaseFiledCusip_ResolvesToTheStock(bool retainedOwnRows)
    {
        var reportDate = new DateOnly(2026, 3, 31);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "BBUC",
            Name: "Brookfield Business Corp",
            Cik: "1654795",
            Cusip: "11259V106"
        );
        using (var seed = FreshContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            // The filing's own earlier import, stored with the spelling the filer used.
            if (retainedOwnRows)
                seed.Add(
                    new InstitutionalHolding
                    {
                        EquityIssuerId = stock.Id,
                        InstitutionalHolder = new InstitutionalHolder
                        {
                            Cik = "1142031",
                            Name = "Private Management Group",
                        },
                        FilingDate = new DateOnly(2026, 5, 8),
                        ReportDate = reportDate,
                        FilingType = FilingType.Form13F,
                        AccessionNumber = "ACC-001",
                        Cusip = "11259v106",
                        Shares = 1,
                        Value = 32,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-001\t2026-05-08\t2026-03-31\t0001142031\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-001\tN\tPrivate Management Group\tIrvine\tCA\t028-04556\t105909\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-001\t11259v106\t921231\tSH\t\tSOLE\t921231\t0\t0\tCL A EXC SUB VTG\t\n";
        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );
        var prices = Substitute.For<IStockPriceProvider>();
        prices
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    new Dictionary<(Guid, string, DateOnly), decimal>
                    {
                        [(stock.Id, null, reportDate)] = 32m,
                    }
                )
            );
        var sut = new HoldingsImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            prices,
            Substitute.For<MassTransit.IBus>()
        );

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2026, 1, 1),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();
        result.UntrackedPositionsOnly.Should().BeFalse();
        using var verify = FreshContext();
        var holding = await verify.Set<InstitutionalHolding>().SingleAsync();
        holding.EquityIssuerId.Should().Be(stock.Id);
        holding.Shares.Should().Be(921231);
        holding
            .Cusip.Should()
            .Be(
                "11259v106",
                "the filed spelling is stored; retained-observation matching compares it exactly"
            );
        (await verify.Set<UnmappedCusip>().AnyAsync())
            .Should()
            .BeFalse("a resolved CUSIP never lands on the unmapped worklist");
    }
}
