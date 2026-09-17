using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Integrations.XbrlFilings;
using Equibles.Media.Data;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Esef;

// The whole corpus is read and matched by legal entity identifier; the fixture is TotalEnergies' own eight
// filings, which hold the FR/GB pair the country rule exists for. See TestAssets/Esef/README.md.
public class EsefReportImportServiceTests
{
    private const string Lei = "529900S21EQ1BO4ESM68";
    private const string SecondLei = "259400NFU8A8SBP6VC21";
    private const string LatestFrenchReport =
        "/529900S21EQ1BO4ESM68/2025-12-31/ESEF/FR/0/529900S21EQ1BO4ESM68-2025-12-31-1-fr/reports/529900S21EQ1BO4ESM68-2025-12-31-1-fr.xhtml";
    private const string LatestBritishReport =
        "/529900S21EQ1BO4ESM68/2025-12-31/ESEF/GB/0/529900S21EQ1BO4ESM68-2025-12-31/reports/529900S21EQ1BO4ESM68-2025-12-31.xhtml";

    [Fact]
    public async Task Import_StoresTheLatestReportOfAVerifiedIssuer()
    {
        var harness = await Harness.Create(Issuer("FR"));

        await harness.Service.Import(CancellationToken.None);

        var save = harness.Saved.Should().ContainSingle().Subject;
        save.DocumentType.Should().Be(DocumentType.EsefAnnualReport);
        save.AccessionNumber.Should().Be("529900S21EQ1BO4ESM68-20251231-FR");
        save.AccessionNumber.Length.Should().Be(EsefFilingSelection.FilingReferenceLength);
        save.ReportingForDate.Should().Be(new DateOnly(2025, 12, 31));
        // The index states when it received the report, which is the only date beyond the period it gives.
        save.ReportingDate.Should().Be(new DateOnly(2026, 4, 7));
        save.SourceUrl.Should().Be("https://filings.xbrl.org" + LatestFrenchReport);
        save.Xbrl.Status.Should().Be(XbrlCaptureStatus.Captured);
        save.Xbrl.Type.Should().Be(XbrlType.InlineIxbrl);
        save.Xbrl.RawBytes.Should().Equal(harness.ReportBytes);
        // A European report has no SEC rendering of its statements, so that capture lane never queues it.
        save.ReportedStatements.Should().Be(XbrlCaptureStatus.NotPresent);
        // The retrieval body is the report's text, never its markup or its encoded payloads.
        var content = System.Text.Encoding.UTF8.GetString(save.Content);
        content.Should().Contain("| Aktywa razem |  | 693 232 | 720 184 |");
        content.Should().NotContain("base64");
    }

    [Theory]
    [InlineData("FR", LatestFrenchReport, "529900S21EQ1BO4ESM68-20251231-FR")]
    [InlineData("GB", LatestBritishReport, "529900S21EQ1BO4ESM68-20251231-GB")]
    public async Task Import_TakesTheFilingMadeInTheIssuersOwnMarketCountry(
        string marketCountry,
        string expectedReport,
        string expectedReference
    )
    {
        var harness = await Harness.Create(Issuer(marketCountry));

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().ContainSingle().Which.AccessionNumber.Should().Be(expectedReference);
        harness.Handler.Requests.Select(uri => uri.PathAndQuery).Should().Contain(expectedReport);
    }

    [Fact]
    public async Task Import_WhenTheReportIsAlreadyStored_CapturesNothing()
    {
        var issuer = Issuer("FR");
        var harness = await Harness.Create(issuer);
        harness.Context.Add(
            new Document
            {
                EquityIssuerId = issuer.Id,
                DocumentType = DocumentType.EsefAnnualReport,
                AccessionNumber = "529900S21EQ1BO4ESM68-20251231-FR",
                ReportingDate = new DateOnly(2026, 4, 7),
                ReportingForDate = new DateOnly(2025, 12, 31),
                Content = new Equibles.Media.Data.Models.File { Name = "stored.txt" },
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        // The index is still read: a later period would be captured on the same pass.
        harness.Handler.Requests.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Import_AnIssuerThatAlsoFilesWithTheSec_IsLeftToThatLane()
    {
        var issuer = Issuer("FR");
        issuer.Cik = "0000320193";
        var harness = await Harness.Create(issuer);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_RecordsTheFiscalYearEndTheReportsOwnPeriodStates()
    {
        var harness = await Harness.Create(Issuer("FR"));

        await harness.Service.Import(CancellationToken.None);

        harness.Context.ChangeTracker.Clear();
        var stored = await harness.Context.Set<EquityIssuer>().SingleAsync();
        stored.FiscalYearEndMonth.Should().Be(12);
        stored.FiscalYearEndDay.Should().Be(31);
    }

    [Fact]
    public async Task Import_LeavesARecordedFiscalYearEndAlone()
    {
        var issuer = Issuer("FR");
        issuer.FiscalYearEndMonth = 3;
        issuer.FiscalYearEndDay = 31;
        var harness = await Harness.Create(issuer);

        await harness.Service.Import(CancellationToken.None);

        harness.Context.ChangeTracker.Clear();
        var stored = await harness.Context.Set<EquityIssuer>().SingleAsync();
        stored.FiscalYearEndMonth.Should().Be(3);
        stored.FiscalYearEndDay.Should().Be(31);
    }

    [Fact]
    public async Task Import_RecordsTheFiscalYearEndEvenWhenTheDocumentCannotBeStored()
    {
        var harness = await Harness.Create(Issuer("FR"), saveThrows: true);

        await harness.Service.Import(CancellationToken.None);

        // The pass survives one issuer's failure, and the calendar the report states is already recorded,
        // so the retry that stores the document cannot label its facts from a missing calendar.
        harness.Saved.Should().BeEmpty();
        harness.Context.ChangeTracker.Clear();
        var stored = await harness.Context.Set<EquityIssuer>().SingleAsync();
        stored.FiscalYearEndMonth.Should().Be(12);
        stored.FiscalYearEndDay.Should().Be(31);
    }

    [Fact]
    public async Task Import_AReportAddressedPastTheColumnWidth_IsRefusedBeforeItIsFetched()
    {
        // The real index with one report path lengthened past Document.SourceUrl, which a save would throw
        // on and the pass would then retry every cycle for ever.
        var padding = new string('x', EsefReportImportService.MaxSourceUrlLength);
        var harness = await Harness.Create(
            Issuer("FR"),
            rewriteIndex: index =>
                index.Replace(
                    "529900S21EQ1BO4ESM68-2025-12-31-1-fr.xhtml",
                    padding + "-529900S21EQ1BO4ESM68-2025-12-31-1-fr.xhtml"
                )
        );

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        harness.Handler.Requests.Should().ContainSingle();
    }

    // The lane stores nothing the extraction sweep would refuse to parse, so the two ceilings are one
    // number. A report past it yields no fact and the reader refuses it too.
    [Fact]
    public void TheCaptureCeilingIsTheExtractionSweepsOwnParseCeiling() =>
        ((long)EsefReportImportService.MaxReportBytes)
            .Should()
            .Be(
                Equibles
                    .Sec
                    .FinancialFacts
                    .HostedService
                    .Services
                    .XbrlFactExtractionService
                    .MaxParseableEnvelopeBytes
            );

    [Fact]
    public async Task Import_LeavesNothingTrackedWhenAnIssuersDocumentCannotBeStored()
    {
        // The save tracks its rows before it commits, and one context serves the whole pass, so a failure
        // that left them tracked would have the next issuer's save flush them outside any transaction.
        var harness = await Harness.Create(Issuer("FR"), saveThrows: true);

        await harness.Service.Import(CancellationToken.None);

        harness.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task Import_StopsAtTheCycleBudget(int budget, int expected)
    {
        var harness = await Harness.CreateTwo(capturesPerCycle: budget);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().HaveCount(expected);
    }

    [Fact]
    public async Task Import_CapturesEachIssuerTheIndexHoldsAFilingFor()
    {
        var harness = await Harness.CreateTwo();

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Select(save => save.AccessionNumber)
            .Should()
            .BeEquivalentTo([
                "529900S21EQ1BO4ESM68-20251231-FR",
                "259400NFU8A8SBP6VC21-20251231-FR",
            ]);
    }

    [Fact]
    public async Task Import_ReadsEveryPageTheIndexSaysItHas()
    {
        // The real corpus is about 260 pages, so the pass must page through it rather than read the first
        // and stop. The stated count is what ends it.
        var harness = await Harness.CreateTwo(splitAcrossPages: true);

        await harness.Service.Import(CancellationToken.None);

        harness
            .Handler.Requests.Select(uri => uri.Query)
            .Should()
            .Contain(query => query.Contains("page%5Bnumber%5D=2"));
        harness.Saved.Should().HaveCount(2);
    }

    [Fact]
    public async Task Import_AnIssuerWithNoFilingInTheIndex_IsNotAFailure()
    {
        var issuer = Issuer("FR");
        issuer.LegalEntityIdentifier = "213800JQMQK3RCVSHT68";
        var harness = await Harness.Create(issuer);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        harness.Handler.Requests.Should().ContainSingle();
    }

    // Serves the index as one page or as one row a page, keeping the stated total, so the pass has to
    // follow the page numbers to see every row.
    private static List<string> SplitPages(string index, bool split)
    {
        if (!split)
            return [index];
        using var document = System.Text.Json.JsonDocument.Parse(index);
        var root = document.RootElement;
        var included = root.GetProperty("included").GetRawText();
        var count = root.GetProperty("data").GetArrayLength();
        return root.GetProperty("data")
            .EnumerateArray()
            .Select(row =>
                $$"""
                    {"meta":{"count":{{count}}},"data":[{{row.GetRawText()}}],"included":{{included}}}
                    """
            )
            .ToList();
    }

    private static EquityIssuer Issuer(string marketCountry) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "TTE",
            Name: "TotalEnergies SE",
            LegalEntityIdentifier: Lei,
            Isin: "FR0000120271",
            MarketCountryCode: marketCountry,
            MarketIdentifierCode: marketCountry == "FR" ? "XPAR" : "XLON",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );

    [Fact]
    public async Task Import_AReportPastTheCeiling_IsRememberedSoItsBytesAreNotFetchedAgain()
    {
        var harness = await Harness.Create(Issuer("FR"));
        harness.Handler.OverstatedContentLength = 200_000_000;
        harness.Handler.OverstatedPaths.Add(LatestFrenchReport);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        harness.Context.ChangeTracker.Clear();
        var refusal = await harness.Context.Set<EsefOversizedReport>().SingleAsync();
        refusal.Reference.Should().Be("529900S21EQ1BO4ESM68-20251231-FR");
        refusal.CeilingBytes.Should().Be(EsefReportImportService.MaxReportBytes);
        refusal.SourceUrl.Should().Be("https://filings.xbrl.org" + LatestFrenchReport);

        harness.Handler.Requests.Clear();
        await harness.Service.Import(CancellationToken.None);

        // The index is read again, because a later period would still be captured. The report's own
        // address is not: the host states no length, so reaching that refusal again costs the download
        // again, which is the whole reason the row exists.
        harness.Handler.Requests.Should().NotBeEmpty();
        harness
            .Handler.Requests.Select(uri => uri.AbsolutePath)
            .Should()
            .NotContain(LatestFrenchReport);
    }

    [Fact]
    public async Task Import_ARefusalSpendsTheCycleBudget()
    {
        // A refusal costs the whole download here, so it has to be charged; it is safe to charge only
        // because it is remembered, so one report spends the budget once rather than every cycle.
        var harness = await Harness.CreateTwo(capturesPerCycle: 1);
        harness.Handler.OverstatedContentLength = 200_000_000;
        harness.Handler.OverstatedPaths.Add(LatestFrenchReport);
        harness.Handler.OverstatedPaths.Add(LatestFrenchReport.Replace(Lei, SecondLei));

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        harness
            .Handler.Requests.Where(uri => uri.AbsolutePath.EndsWith(".xhtml"))
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task Import_AReportRefusedUnderALowerCeiling_IsTriedAgain()
    {
        // The row records the ceiling the report exceeded, never its size, which this host never states.
        // Raising the ceiling therefore has to re-open every filing refused under a lower one.
        var harness = await Harness.Create(Issuer("FR"));
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                CeilingBytes = EsefReportImportService.MaxReportBytes - 1,
                RefusedAt = DateTime.UtcNow,
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().ContainSingle();
    }

    private sealed record SavedDocument(
        Guid IssuerId,
        byte[] Content,
        DocumentType DocumentType,
        DateOnly ReportingDate,
        DateOnly ReportingForDate,
        string SourceUrl,
        string AccessionNumber,
        XbrlCaptureResult Xbrl,
        XbrlCaptureStatus ReportedStatements
    );

    private sealed class Harness
    {
        public EquiblesFinancialDbContext Context { get; private init; }
        public EsefReportImportService Service { get; private set; }
        public EsefIndexTestHandler Handler { get; private init; }
        public List<SavedDocument> Saved { get; } = [];
        public byte[] ReportBytes { get; private init; }

        // Two issuers over the derived two-issuer index; see TestAssets/Esef/README.md.
        public static Task<Harness> CreateTwo(
            int capturesPerCycle = 100,
            bool splitAcrossPages = false
        ) =>
            Create(
                Issuer("FR"),
                capturesPerCycle,
                second: Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: Guid.NewGuid(),
                    Ticker: "IZS",
                    Name: "Izostal S.A.",
                    LegalEntityIdentifier: "259400NFU8A8SBP6VC21",
                    Isin: "PLIZSTL00013",
                    MarketCountryCode: "FR",
                    MarketIdentifierCode: "XPAR",
                    IdentityState: EquityIdentityState.Verified,
                    TradingCurrency: "EUR",
                    QuoteUnitMultiplier: 1m
                ),
                splitAcrossPages: splitAcrossPages
            );

        public static async Task<Harness> Create(
            EquityIssuer issuer,
            int capturesPerCycle = 100,
            bool saveThrows = false,
            Func<string, string> rewriteIndex = null,
            EquityIssuer second = null,
            bool splitAcrossPages = false
        )
        {
            var context = NewDb();
            context.Add(issuer);
            if (second != null)
                context.Add(second);
            await context.SaveChangesAsync();

            var index = File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Esef",
                    second == null ? "filings-one-issuer.json" : "filings-two-issuers.json"
                )
            );
            if (rewriteIndex != null)
                index = rewriteIndex(index);
            var report = File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Esef",
                    "izs-2022-excerpt.xhtml"
                )
            );
            var reportText = System.Text.Encoding.UTF8.GetString(report);
            var pages = SplitPages(index, splitAcrossPages);
            var bodies = new Dictionary<string, string>
            {
                [LatestFrenchReport] = reportText,
                [LatestBritishReport] = reportText,
                [LatestFrenchReport.Replace(Lei, SecondLei)] = reportText,
                [LatestBritishReport.Replace(Lei, SecondLei)] = reportText,
            };
            for (var page = 1; page <= pages.Count; page++)
            {
                bodies[
                    XbrlFilingsClient
                        .IndexUrl(page, EsefReportImportService.IndexPageSize)
                        .PathAndQuery
                ] = pages[page - 1];
            }
            var handler = new EsefIndexTestHandler(bodies);
            var client = new XbrlFilingsClient(new HttpClient(handler))
            {
                Pace = new Equibles.Integrations.Common.RateLimiter.RateLimiter(
                    1000,
                    TimeSpan.FromSeconds(1)
                ),
            };

            var harness = new Harness
            {
                Context = context,
                Handler = handler,
                ReportBytes = report,
            };
            var persistence = Substitute.For<IDocumentPersistenceService>();
            persistence
                .Save(
                    Arg.Any<EquityIssuer>(),
                    Arg.Any<byte[]>(),
                    Arg.Any<string>(),
                    Arg.Any<DocumentType>(),
                    Arg.Any<DateOnly>(),
                    Arg.Any<DateOnly>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<XbrlCaptureResult>(),
                    Arg.Any<AsFiledHtmlCaptureResult>(),
                    Arg.Any<XbrlCaptureStatus>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    if (saveThrows)
                    {
                        // What the real save does before it commits, so a missing cleanup is visible.
                        harness.Context.Add(
                            new Equibles.Media.Data.Models.File { Name = "half-written.txt" }
                        );
                        throw new InvalidOperationException("the document could not be stored");
                    }
                    harness.Saved.Add(
                        new SavedDocument(
                            call.Arg<EquityIssuer>().Id,
                            call.ArgAt<byte[]>(1),
                            call.ArgAt<DocumentType>(3),
                            call.ArgAt<DateOnly>(4),
                            call.ArgAt<DateOnly>(5),
                            call.ArgAt<string>(6),
                            call.ArgAt<string>(7),
                            call.ArgAt<XbrlCaptureResult>(9),
                            call.ArgAt<XbrlCaptureStatus>(11)
                        )
                    );
                    return Task.CompletedTask;
                });

            var issuers = new EquityIssuerRepository(context);
            harness.Service = new EsefReportImportService(
                client,
                issuers,
                new DocumentRepository(context),
                new EsefOversizedReportRepository(context),
                persistence,
                new EquityIdentityManager(issuers, Substitute.For<IBus>()),
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter(),
                Options.Create(
                    new EsefReportScraperOptions { MaxCapturesPerCycle = capturesPerCycle }
                ),
                NullLogger<EsefReportImportService>.Instance
            );
            return harness;
        }
    }

    // The Sec module also maps the chunk embeddings, whose pgvector type the in-memory provider cannot
    // construct. This lane writes documents and reads them back, so only that half is registered.
    private sealed class DocumentsOnlySecModule : IModuleConfiguration
    {
        public void ConfigureEntities(ModelBuilder builder)
        {
            builder.Entity<Document>(entity =>
            {
                entity
                    .Property(document => document.DocumentType)
                    .HasConversion(
                        new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<
                            DocumentType,
                            string
                        >(value => value.Value, value => DocumentType.FromValue(value))
                    );
                entity.Ignore(document => document.Chunks);
                entity.Ignore(document => document.Images);
                entity.Ignore(document => document.Artifacts);
            });
            builder.Entity<EsefOversizedReport>();
            builder.Ignore<Equibles.Sec.Data.Models.Chunks.Chunk>();
            builder.Ignore<Equibles.Sec.Data.Models.Chunks.Embedding>();
        }
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new DocumentsOnlySecModule(),
            }
        );
        context.Database.EnsureCreated();
        return context;
    }
}
