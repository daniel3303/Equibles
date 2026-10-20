using System.Globalization;
using System.Net;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The 13D/13G coverage walk against recorded EDGAR documents: Vanguard's two 2026 13G/As on
/// NVIDIA, which the first backfill lost, and Allspring's same-day 13G and 13G/A on Innospec.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Schedule13DGCoverageServiceTests : IAsyncLifetime
{
    private const string NvidiaCusip = "67066G104";
    private const string InnospecCusip = "45768S105";
    private const string VanguardNvidia = "0000102909-26-000226";
    private const string VanguardNvidiaExit = "0000102909-26-000426";
    private const string AllspringOriginal = "0001890906-26-000068";
    private const string AllspringAmendment = "0001890906-26-000074";
    private static readonly DateOnly MinReportDate = new(2020, 1, 1);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;
    private readonly ISecEdgarClient _edgar = Substitute.For<ISecEdgarClient>();
    private readonly Dictionary<DateOnly, List<EdgarDailyIndexEntry>> _index = [];

    public Schedule13DGCoverageServiceTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        _edgar
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => _index.GetValueOrDefault(call.ArgAt<DateOnly>(0)) ?? []);
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task BackfillDays_ReadsOnlyAccessionsWithNothingOnFile_AndImportsEachEventDate()
    {
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        // Each filing is listed under its filer and under its subject company.
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909", "1045810");
        List(new DateOnly(2026, 3, 26), VanguardNvidiaExit, "102909", "1045810");
        List(new DateOnly(2026, 3, 26), "0000000001-26-000001", "1");
        List(new DateOnly(2026, 3, 26), "0000000002-26-000002", "2");
        await SeedDetails("0000000001-26-000001");
        await SeedProcessed("0000000002-26-000002");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        Serve(VanguardNvidiaExit, "sc13ga-vanguard-nvidia-exit.xml");

        var result = await Coverage()
            .BackfillDays(
                new DateOnly(2026, 1, 30),
                new DateOnly(2026, 3, 31),
                MinReportDate,
                100,
                CancellationToken.None
            );

        result.Should().Be(new Schedule13DGBackfillResult(new DateOnly(2026, 3, 31), 2, 2, false));
        (await Positions(NvidiaCusip))
            .Should()
            .Equal(
                (VanguardNvidia, new DateOnly(2025, 12, 31), 2_266_625_855L, "102909"),
                (VanguardNvidiaExit, new DateOnly(2026, 3, 13), 0L, "102909")
            );
        await _edgar
            .Received(2)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BackfillDays_OriginalFoundAfterItsStoredAmendment_KeepsTheAmendment()
    {
        await SeedIssuer("IOSP", "0001054905", InnospecCusip);
        var amendment = Parse("sc13ga-allspring-innospec.xml", AllspringAmendment);
        (await Ingestion().ImportParsedFiling(amendment, MinReportDate, CancellationToken.None))
            .Should()
            .Be(Schedule13DGImportOutcome.Imported);
        List(new DateOnly(2026, 10, 7), AllspringOriginal, "1890906", "1054905");
        Serve(AllspringOriginal, "sc13g-allspring-innospec.xml");

        await Coverage()
            .BackfillDays(
                new DateOnly(2026, 10, 7),
                new DateOnly(2026, 10, 7),
                MinReportDate,
                100,
                CancellationToken.None
            );

        await using var verify = FreshContext();
        var stored = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.Cusip == InnospecCusip)
            .SingleAsync();
        stored.AccessionNumber.Should().Be(AllspringAmendment);
        stored.IsAmendment.Should().BeTrue();
        (await verify.Set<ProcessedFiling>().Select(p => p.AccessionNumber).ToListAsync())
            .Should()
            .BeEquivalentTo([AllspringOriginal, AllspringAmendment]);
        (await verify.Set<BeneficialOwnershipFiling>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task BackfillDays_EdgarUnavailableMidDay_StopsBeforeThatDayAndKeepsItsWork()
    {
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        List(new DateOnly(2026, 3, 26), VanguardNvidiaExit, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidiaExit,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests)
            );

        var result = await Coverage()
            .BackfillDays(
                new DateOnly(2026, 1, 30),
                new DateOnly(2026, 3, 31),
                MinReportDate,
                100,
                CancellationToken.None
            );

        result.CompletedThrough.Should().Be(new DateOnly(2026, 3, 25));
        result.Stopped.Should().BeTrue();
        (await Positions(NvidiaCusip)).Select(p => p.Accession).Should().Equal(VanguardNvidia);
        await using var verify = FreshContext();
        (await verify.Set<BeneficialOwnershipFiling>().Select(f => f.AccessionNumber).ToListAsync())
            .Should()
            .Equal(VanguardNvidia);
    }

    [Fact]
    public async Task BackfillDays_StopsAfterTheDayThatReachesTheDocumentBudget()
    {
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        List(new DateOnly(2026, 3, 26), VanguardNvidiaExit, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        Serve(VanguardNvidiaExit, "sc13ga-vanguard-nvidia-exit.xml");

        var result = await Coverage()
            .BackfillDays(
                new DateOnly(2026, 1, 30),
                new DateOnly(2026, 3, 31),
                MinReportDate,
                1,
                CancellationToken.None
            );

        result.Should().Be(new Schedule13DGBackfillResult(new DateOnly(2026, 1, 30), 1, 1, false));
    }

    [Fact]
    public async Task BackfillDays_DocumentEdgarDoesNotServe_IsRecordedOnceAndNotReadAgain()
    {
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidia,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([]);

        var day = new DateOnly(2026, 1, 30);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);

        await using var verify = FreshContext();
        var detail = await verify.Set<BeneficialOwnershipFiling>().SingleAsync();
        detail.IsUnreadable.Should().BeTrue();
        detail.FilingType.Should().Be(FilingType.Schedule13G);
        await _edgar
            .Received(1)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RetryResolvable_FilingReadBeforeItsIssuerWasTracked_ImportsOnceItIs()
    {
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        var day = new DateOnly(2026, 1, 30);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        (await Positions(NvidiaCusip)).Should().BeEmpty();
        await AgeDetails(TimeSpan.FromDays(2));

        // Untracked, the stored details never cost another document read.
        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        var imported = await Coverage()
            .RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default);

        imported.Should().Be(1);
        (await Positions(NvidiaCusip))
            .Should()
            .Equal((VanguardNvidia, new DateOnly(2025, 12, 31), 2_266_625_855L, "102909"));
        await _edgar
            .Received(2)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidia,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RetryResolvable_FilingReadWithinTheBackoff_WaitsForIt()
    {
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        var day = new DateOnly(2026, 1, 30);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);

        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);
        (await Positions(NvidiaCusip)).Should().BeEmpty();
    }

    [Fact]
    public async Task RetryResolvable_EarlierFilingOfAStoredStake_IsNotReadAgain()
    {
        await SeedIssuer("IOSP", "0001054905", InnospecCusip);
        var original = Parse("sc13g-allspring-innospec.xml", AllspringOriginal);
        await Writer().Write(original, CancellationToken.None);
        var amendment = Parse("sc13ga-allspring-innospec.xml", AllspringAmendment);
        await Ingestion().ImportParsedFiling(amendment, MinReportDate, CancellationToken.None);
        await AgeDetails(TimeSpan.FromDays(2));

        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);
        await _edgar
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    private void List(DateOnly day, string accession, params string[] ciks)
    {
        if (!_index.TryGetValue(day, out var entries))
            _index[day] = entries = [];
        entries.AddRange(
            ciks.Select(cik => new EdgarDailyIndexEntry
            {
                FormType = "SCHEDULE 13G/A",
                CompanyName = $"Company {cik}",
                Cik = cik,
                DateFiled = day,
                AccessionNumber = accession,
            })
        );
    }

    private void Serve(string accession, string asset) =>
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                accession,
                "primary_doc.xml",
                Arg.Any<CancellationToken>()
            )
            .Returns(Encoding.UTF8.GetBytes(Asset(asset)));

    private static string Asset(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Holdings13DG", name)
        );

    private static Parsed13DGFiling Parse(string asset, string accession) =>
        new Filing13DGXmlParser().ParseFiling(
            Asset(asset),
            accession,
            "1890906",
            new DateOnly(2026, 10, 7)
        );

    private async Task SeedIssuer(string ticker, string cik, string cusip)
    {
        await using var seed = FreshContext();
        seed.Set<EquityIssuer>()
            .Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: Guid.NewGuid(),
                    Ticker: ticker,
                    Name: ticker,
                    Cik: cik,
                    Cusip: cusip
                )
            );
        await seed.SaveChangesAsync();
    }

    private async Task SeedDetails(string accession)
    {
        await using var seed = FreshContext();
        seed.Add(
            new BeneficialOwnershipFiling
            {
                AccessionNumber = accession,
                FilingType = FilingType.Schedule13G,
                FilingDate = new DateOnly(2026, 3, 26),
                IsUnreadable = true,
            }
        );
        await seed.SaveChangesAsync();
    }

    private async Task SeedProcessed(string accession)
    {
        await using var seed = FreshContext();
        seed.Add(new ProcessedFiling { AccessionNumber = accession });
        await seed.SaveChangesAsync();
    }

    private async Task AgeDetails(TimeSpan age)
    {
        await using var ctx = FreshContext();
        var readAt = DateTime.UtcNow - age;
        await ctx.Set<BeneficialOwnershipFiling>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.ReadAt, readAt));
    }

    private async Task<
        List<(string Accession, DateOnly ReportDate, long Shares, string HolderCik)>
    > Positions(string cusip)
    {
        await using var verify = FreshContext();
        var rows = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.Cusip == cusip)
            .OrderBy(h => h.ReportDate)
            .Select(h => new
            {
                h.AccessionNumber,
                h.ReportDate,
                h.Shares,
                h.InstitutionalHolder.Cik,
                h.FilingType,
            })
            .ToListAsync();
        rows.Should().OnlyContain(r => r.FilingType == FilingType.Schedule13G);
        return rows.Select(r => (r.AccessionNumber, r.ReportDate, r.Shares, r.Cik)).ToList();
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
                sp.GetService(typeof(ProcessedFilingRepository))
                    .Returns(new ProcessedFilingRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private BeneficialOwnershipDetailWriter Writer() =>
        new(CreateScopeFactory(), Substitute.For<ILogger<BeneficialOwnershipDetailWriter>>());

    private Realtime13DGIngestionService Ingestion()
    {
        var scopeFactory = CreateScopeFactory();
        var prices = Substitute.For<IStockPriceProvider>();
        prices
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    call.ArgAt<IEnumerable<(Guid, string, DateOnly)>>(0)
                        .Distinct()
                        .ToDictionary(key => key, _ => 100m)
                )
            );
        var importService = new HoldingsImportService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            prices,
            Substitute.For<MassTransit.IBus>()
        );
        return new Realtime13DGIngestionService(
            _edgar,
            new Filing13DGXmlParser(),
            new Realtime13DGArchiveBuilder(),
            importService,
            Writer(),
            scopeFactory,
            Substitute.For<ILogger<Realtime13DGIngestionService>>()
        );
    }

    private Schedule13DGCoverageService Coverage() =>
        new(
            _edgar,
            new Filing13DGXmlParser(),
            Ingestion(),
            Writer(),
            CreateScopeFactory(),
            Substitute.For<ILogger<Schedule13DGCoverageService>>()
        );
}
