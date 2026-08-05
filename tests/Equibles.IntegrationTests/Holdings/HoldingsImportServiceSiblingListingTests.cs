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
/// Sibling share classes import as their own rows (#4247). Alphabet is the reference
/// shape: GOOGL's CUSIP (02079K305) lives on the stock, Class C's (02079K107) matched
/// nothing, and every GOOG 13F line — ~5,300 positions a quarter — was dropped at
/// BuildCusipMapping. A <see cref="CommonStockListedCusip"/> row resolves the sibling
/// CUSIP to the same filer WITHOUT collapsing the two securities: the holding row is
/// keyed by ListedTicker and valued from the class's own price series. Merging them
/// instead would overwrite one class's position with the other's (the upsert key had
/// no security discriminator) and price BRK-A at BRK-B's close — ~1500x off.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceSiblingListingTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceSiblingListingTests(ParadeDbFixture fixture)
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
        Dictionary<(Guid, string, DateOnly), decimal> prices
    )
    {
        var provider = Substitute.For<IStockPriceProvider>();
        provider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(prices));
        return provider;
    }

    private static ZipArchive AlphabetBothClassesArchive()
    {
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-201\t2026-05-08\t2026-03-31\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-201\tN\tSample Capital\tOmaha\tNE\t028-12345\t12345\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-201\t02079K305\t1000\tSH\t\tSOLE\t1000\t0\t0\tCL A\t\n"
            + "ACC-201\t02079K107\t500\tSH\t\tSOLE\t0\t0\t500\tCL C\t\n";

        return BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );
    }

    private async Task<CommonStock> SeedAlphabet()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "GOOGL",
            Name = "Alphabet Inc",
            Cik = "1652044",
            Cusip = "02079K305",
            SecondaryTickers = ["GOOG"],
        };
        using var seed = FreshContext();
        seed.Set<CommonStock>().Add(stock);
        seed.Set<CommonStockListedCusip>()
            .Add(
                new CommonStockListedCusip
                {
                    CommonStockId = stock.Id,
                    ListedTicker = "GOOG",
                    Cusip = "02079K107",
                }
            );
        await seed.SaveChangesAsync();
        return stock;
    }

    [Fact]
    public async Task ImportDataSet_BothClassesFiledSameQuarter_ImportsTwoRowsEachOnItsOwnPrice()
    {
        var stock = await SeedAlphabet();
        var reportDate = new DateOnly(2026, 3, 31);

        // Distinct closes so a cross-class pricing bug shows up in the derived values,
        // not just in the row count.
        var prices = new Dictionary<(Guid, string, DateOnly), decimal>
        {
            [(stock.Id, null, reportDate)] = 170m,
            [(stock.Id, "GOOG", reportDate)] = 172m,
        };
        using var archive = AlphabetBothClassesArchive();
        var sut = CreateImporter(PriceProviderReturning(prices));

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2026, 1, 1),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();

        using var verify = FreshContext();
        var holdings = await verify.Set<InstitutionalHolding>().ToListAsync();
        holdings.Should().HaveCount(2, "the two classes are two securities, never one row");

        var primary = holdings.Single(h => h.ListedTicker == null);
        primary.CommonStockId.Should().Be(stock.Id);
        primary.Cusip.Should().Be("02079K305");
        primary.Shares.Should().Be(1000);
        primary.Value.Should().Be(170_000L);
        primary.ValuePending.Should().BeFalse();

        var classC = holdings.Single(h => h.ListedTicker == "GOOG");
        classC.CommonStockId.Should().Be(stock.Id);
        classC.Cusip.Should().Be("02079K107");
        classC.Shares.Should().Be(500);
        classC.Value.Should().Be(86_000L, "the Class C row prices at ITS class's close");
        classC.ValuePending.Should().BeFalse();

        var unmapped = await verify.Set<UnmappedCusip>().ToListAsync();
        unmapped.Should().BeEmpty("the sibling CUSIP resolves instead of accruing as unmapped");
    }

    [Fact]
    public async Task ImportDataSet_SecondaryWithUnattributedPostReportSplit_StaysHonestlyPending()
    {
        // An issuer split captured without per-series attribution proves nothing about the
        // sibling class's own basis. The secondary row must import its SHARES but refuse a
        // value; the primary row values normally with the factor applied.
        var stock = await SeedAlphabet();
        var reportDate = new DateOnly(2026, 3, 31);

        using (var seed = FreshContext())
        {
            seed.Set<StockSplit>()
                .Add(
                    new StockSplit
                    {
                        CommonStockId = stock.Id,
                        EffectiveDate = new DateOnly(2026, 4, 20),
                        Numerator = 20m,
                        Denominator = 1m,
                        PriceSeriesTicker = null,
                        PriceAdjustmentAppliedTime = new DateTime(
                            2026,
                            4,
                            21,
                            0,
                            0,
                            0,
                            DateTimeKind.Utc
                        ),
                    }
                );
            await seed.SaveChangesAsync();
        }

        var prices = new Dictionary<(Guid, string, DateOnly), decimal>
        {
            [(stock.Id, null, reportDate)] = 8.50m,
            [(stock.Id, "GOOG", reportDate)] = 8.60m,
        };
        using var archive = AlphabetBothClassesArchive();
        var sut = CreateImporter(PriceProviderReturning(prices));

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2026, 1, 1),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();

        using var verify = FreshContext();
        var holdings = await verify.Set<InstitutionalHolding>().ToListAsync();
        holdings.Should().HaveCount(2);

        var primary = holdings.Single(h => h.ListedTicker == null);
        primary.ValuePending.Should().BeFalse();
        primary.Value.Should().Be((long)(1000 * 20m * 8.50m));

        var classC = holdings.Single(h => h.ListedTicker == "GOOG");
        classC.Shares.Should().Be(500, "the position itself still imports and displays");
        classC
            .ValuePending.Should()
            .BeTrue("no honest value exists until the class's own basis is known");
        classC.Value.Should().Be(0L);
    }

    [Fact]
    public async Task ImportDataSet_ListedCusipCollidesWithAnotherStocksCurrentCusip_CurrentCusipWins()
    {
        // Precedence pin, mirroring the alias rule: a CURRENT primary assignment outranks
        // another stock's listed-cusip claim on the same CUSIP (a shape only bad data can
        // produce). Primary > alias > listing.
        var owner = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAA",
            Name = "Current Owner Corp",
            Cik = "0000000001",
            Cusip = "999999999",
        };
        var claimant = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BBB",
            Name = "Listing Claimant Corp",
            Cik = "0000000002",
            Cusip = "888888888",
            SecondaryTickers = ["BBB-A"],
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().AddRange(owner, claimant);
            seed.Set<CommonStockListedCusip>()
                .Add(
                    new CommonStockListedCusip
                    {
                        CommonStockId = claimant.Id,
                        ListedTicker = "BBB-A",
                        Cusip = "999999999",
                    }
                );
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2026, 3, 31);
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-202\t2026-05-08\t2026-03-31\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-202\tN\tSample Capital\tOmaha\tNE\t028-12345\t12345\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-202\t999999999\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var prices = new Dictionary<(Guid, string, DateOnly), decimal>
        {
            [(owner.Id, null, reportDate)] = 100m,
            [(claimant.Id, null, reportDate)] = 100m,
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
        holding.CommonStockId.Should().Be(owner.Id);
        holding.ListedTicker.Should().BeNull();
    }
}
