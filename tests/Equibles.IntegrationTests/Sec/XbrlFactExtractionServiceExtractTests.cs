using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// End-to-end pin of the dimensional-fact extraction path (#877): a captured
/// gzipped inline-XBRL envelope goes in, dimensional FinancialFact rows with
/// FinancialFactDimension children come out, and the consolidated fact in the
/// same envelope is left to the Company Facts API (not persisted). Also pins
/// idempotency — re-extracting the same document must not duplicate rows,
/// which exercises the DimensionsKey conflict target end to end against real
/// Postgres.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class XbrlFactExtractionServiceExtractTests : ParadeDbMcpTestBase
{
    private const string Accession = "0000320193-25-000001";

    public XbrlFactExtractionServiceExtractTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Extract_CapturedInlineEnvelope_PersistsDimensionalFactWithChildAndSkipsConsolidated()
    {
        var document = await SeedDocument(InlineEnvelope());
        var sut = BuildSut();

        var persisted = await sut.Extract(document, CancellationToken.None);

        persisted.Should().Be(1);

        var facts = await DbContext
            .Set<FinancialFact>()
            .Include(f => f.Dimensions)
            .Include(f => f.FinancialConcept)
            .Where(f => f.DocumentId == document.Id)
            .ToListAsync(CancellationToken.None);

        var fact = facts.Should().ContainSingle().Subject;
        fact.FinancialConcept.Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        fact.FinancialConcept.Tag.Should()
            .Be("RevenueFromContractWithCustomerExcludingAssessedTax");
        fact.Unit.Should().Be("USD");
        fact.Value.Should().Be(46_222_000_000m);
        fact.AccessionNumber.Should().Be(Accession);
        // Dec-31 FYE + Jan–Mar duration resolves to Q1 2025.
        fact.FiscalYear.Should().Be(2025);
        fact.FiscalPeriod.Should().Be(SecFiscalPeriod.Q1);
        fact.DimensionsKey.Should().MatchRegex("^[0-9a-f]{64}$");

        var dimension = fact.Dimensions.Should().ContainSingle().Subject;
        dimension.Axis.Should().Be("srt:ProductOrServiceAxis");
        dimension.Member.Should().Be("aapl:IPhoneMember");

        // A version replay repairs derived labels without duplicating facts or dimensions.
        var factId = fact.Id;
        fact.FiscalYear = 2024;
        fact.FiscalPeriod = SecFiscalPeriod.Q4;
        await DbContext.SaveChangesAsync();
        var secondRun = await sut.Extract(document, CancellationToken.None);
        secondRun.Should().Be(1);
        await DbContext.Entry(fact).ReloadAsync();
        fact.Id.Should().Be(factId);
        fact.FiscalYear.Should().Be(2025);
        fact.FiscalPeriod.Should().Be(SecFiscalPeriod.Q1);
        (
            await DbContext
                .Set<FinancialFact>()
                .CountAsync(f => f.DocumentId == document.Id, CancellationToken.None)
        )
            .Should()
            .Be(1);
        (
            await DbContext
                .Set<FinancialFactDimension>()
                .CountAsync(d => d.FinancialFact.DocumentId == document.Id, CancellationToken.None)
        )
            .Should()
            .Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extract_CalendarChange_RepairsLabelsUsingTheCapturedHistoricalCalendar(
        bool offPeriodFact
    )
    {
        var envelope = InlineEnvelope()
            .Replace("<html ", "<html xmlns:dei=\"http://xbrl.sec.gov/dei/2025\" ")
            .Replace("scheme=\"cik\"", "scheme=\"http://www.sec.gov/CIK\"")
            .Replace(
                "</body>",
                "<ix:nonNumeric name=\"dei:CurrentFiscalYearEndDate\" contextRef=\"Consolidated\">--12-31</ix:nonNumeric></body>"
            );
        if (offPeriodFact)
            envelope = envelope.Replace(
                "</body>",
                """
                <xbrli:context id="Future"><xbrli:entity><xbrli:identifier scheme="http://www.sec.gov/CIK">0000320193</xbrli:identifier>
                <xbrli:segment><xbrldi:explicitMember dimension="srt:ProductOrServiceAxis">aapl:IPhoneMember</xbrldi:explicitMember></xbrli:segment>
                </xbrli:entity><xbrli:period><xbrli:instant>2025-04-01</xbrli:instant></xbrli:period></xbrli:context>
                <ix:nonFraction name="us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax" contextRef="Future" unitRef="usd">123</ix:nonFraction>
                </body>
                """
            );
        var document = await SeedDocument(envelope);
        await BuildSut().Extract(document, CancellationToken.None);
        var fact = await DbContext
            .Set<FinancialFact>()
            .SingleAsync(f =>
                f.DocumentId == document.Id && f.PeriodEnd == document.ReportingForDate
            );
        var originalId = fact.Id;
        var originalValue = fact.Value;
        var originalUnresolvedPeriod = offPeriodFact
            ? (
                await DbContext
                    .Set<FinancialFact>()
                    .SingleAsync(f =>
                        f.DocumentId == document.Id && f.PeriodEnd == new DateOnly(2025, 4, 1)
                    )
            ).FiscalPeriod
            : default;
        fact.FiscalPeriod = SecFiscalPeriod.Q3;
        document.CommonStock.FiscalYearEndMonth = 6;
        document.CommonStock.FiscalYearEndDay = 30;
        var annual = new Document
        {
            CommonStock = document.CommonStock,
            Content = document.Content,
            DocumentType = DocumentType.TenK,
            ReportingForDate = new(2024, 12, 31),
            ReportingDate = new(2025, 2, 1),
            AccessionNumber = "0000320193-25-000000",
        };
        DbContext.Add(annual);
        DbContext.Add(
            new FinancialFact
            {
                CommonStockId = document.CommonStockId,
                FinancialConceptId = fact.FinancialConceptId,
                Document = annual,
                Unit = "USD",
                PeriodType = FactPeriodType.Duration,
                PeriodStart = new(2024, 1, 1),
                PeriodEnd = new(2024, 12, 31),
                FiscalYear = 2024,
                FiscalPeriod = SecFiscalPeriod.FullYear,
                Value = 100m,
                Form = DocumentType.TenK,
                FiledDate = annual.ReportingDate,
                AccessionNumber = annual.AccessionNumber,
                DimensionsKey = "",
            }
        );
        await DbContext.SaveChangesAsync();
        if (offPeriodFact)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var pending = await Assert.ThrowsAsync<FiscalCalendarEvidencePendingException>(() =>
                    BuildSut(true).Extract(document, CancellationToken.None)
                );
                pending.PersistedCount.Should().Be(1);
                pending.DeferredCount.Should().Be(1);
            }
            (await DbContext.Set<FinancialFact>().CountAsync(f => f.DocumentId == document.Id))
                .Should()
                .Be(2);
            (
                await DbContext
                    .Set<FinancialFactDimension>()
                    .CountAsync(d => d.FinancialFact.DocumentId == document.Id)
            )
                .Should()
                .Be(2);
            var unresolved = await DbContext
                .Set<FinancialFact>()
                .AsNoTracking()
                .SingleAsync(f =>
                    f.DocumentId == document.Id && f.PeriodEnd == new DateOnly(2025, 4, 1)
                );
            unresolved.Value.Should().Be(123m);
            unresolved.FiscalPeriod.Should().Be(originalUnresolvedPeriod);

            var scopes = ServiceScopeSubstitute.Create(
                (typeof(EquiblesFinancialDbContext), DbContext),
                (typeof(DocumentRepository), new DocumentRepository(DbContext)),
                (typeof(XbrlFactExtractionService), BuildSut(true))
            );
            var worker = new XbrlFactsExtractionWorker(
                NullLogger<XbrlFactsExtractionWorker>(),
                scopes,
                new ErrorReporter(scopes, NullLogger<ErrorReporter>()),
                Options.Create(new XbrlFactsExtractionOptions { BatchSize = 1 })
            );
            await (Task)
                typeof(XbrlFactsExtractionWorker)
                    .GetMethod("DoWork", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(worker, [CancellationToken.None])!;
            document.XbrlFactsAttempts.Should().Be(Document.MaxXbrlFactsAttempts);
            document.XbrlFactsVersion.Should().BeLessThan(XbrlFactExtractionService.CurrentVersion);
            (await DbContext.Set<Error>().CountAsync()).Should().Be(0);
            (
                await XbrlFactsExtractionWorker
                    .SelectDueDocuments(
                        DbContext
                            .Set<Document>()
                            .Where(d => d.XbrlStatus == XbrlCaptureStatus.Captured),
                        DbContext.Set<FinancialFactsSyncStatus>()
                    )
                    .CountAsync()
            )
                .Should()
                .Be(0);
        }
        else
            (await BuildSut(true).Extract(document, CancellationToken.None)).Should().Be(1);
        await DbContext.Entry(fact).ReloadAsync();
        fact.Id.Should().Be(originalId);
        fact.Value.Should().Be(originalValue);
        fact.FiscalYear.Should().Be(2025);
        fact.FiscalPeriod.Should().Be(SecFiscalPeriod.Q1);
        document.CommonStock.FiscalYearEndMonth.Should().Be(6);
    }

    private XbrlFactExtractionService BuildSut(bool historicalCalendar = false)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (typeof(FinancialConceptRepository), new FinancialConceptRepository(DbContext))
        );
        var fileManager = Substitute.For<IFileManager>();
        fileManager.GetContent(Arg.Any<File>()).Returns(ci => ((File)ci[0]).FileContent.Bytes);
        return new XbrlFactExtractionService(
            scopeFactory,
            new InlineXbrlParser(),
            new StandaloneXbrlParser(),
            fileManager,
            NullLogger<XbrlFactExtractionService>(),
            historicalCalendar
                ? new FiscalCalendarEvidenceReader(
                    scopeFactory,
                    fileManager,
                    new InlineXbrlParser()
                )
                : null
        );
    }

    [Fact]
    public async Task Extract_ForeignReport_FillsMissingConsolidatedFactWithoutOverwritingExistingRow()
    {
        var document = await SeedDocument(
            InlineEnvelope().Replace("scheme=\"cik\"", "scheme=\"http://www.sec.gov/CIK\"")
        );
        document.DocumentType = DocumentType.SixK;
        await DbContext.SaveChangesAsync();
        var sut = BuildSut();

        (await sut.Extract(document, CancellationToken.None)).Should().Be(2);
        var consolidated = await DbContext
            .Set<FinancialFact>()
            .SingleAsync(f => f.DocumentId == document.Id && f.DimensionsKey == "");
        consolidated.Form.Should().Be(DocumentType.SixK);
        consolidated.PeriodStart.Should().Be(new DateOnly(2025, 1, 1));
        consolidated.PeriodEnd.Should().Be(new DateOnly(2025, 3, 31));
        var id = consolidated.Id;

        // Model an authoritative API row occupying this exact natural key.
        consolidated.Value = 999m;
        consolidated.FiscalYear = 2024;
        consolidated.FiscalPeriod = SecFiscalPeriod.Q4;
        consolidated.DocumentId = null;
        consolidated.Frame = "CY2025Q1";
        await DbContext.SaveChangesAsync();
        await sut.Extract(document, CancellationToken.None);
        await DbContext.Entry(consolidated).ReloadAsync();

        consolidated.Id.Should().Be(id);
        consolidated.Value.Should().Be(999m);
        consolidated.FiscalYear.Should().Be(2025);
        consolidated.FiscalPeriod.Should().Be(SecFiscalPeriod.Q1);
        consolidated.DocumentId.Should().BeNull();
        consolidated.Frame.Should().Be("CY2025Q1");
        (
            await DbContext
                .Set<FinancialFact>()
                .CountAsync(f => f.CommonStockId == document.CommonStockId)
        )
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task Extract_ForeignInstantWithoutFye_PreservesExistingAnnualIdentity()
    {
        var envelope = InlineEnvelope()
            .Replace("scheme=\"cik\"", "scheme=\"http://www.sec.gov/CIK\"")
            .Replace(
                "<xbrli:startDate>2025-01-01</xbrli:startDate><xbrli:endDate>2025-03-31</xbrli:endDate>",
                "<xbrli:instant>2025-12-31</xbrli:instant>"
            );
        var document = await SeedDocument(envelope);
        document.DocumentType = DocumentType.SixK;
        document.ReportingDate = new DateOnly(2026, 2, 1);
        document.ReportingForDate = new DateOnly(2025, 12, 31);
        document.CommonStock.FiscalYearEndMonth = null;
        document.CommonStock.FiscalYearEndDay = null;
        await DbContext.SaveChangesAsync();
        var sut = BuildSut();
        await sut.Extract(document, CancellationToken.None);
        var consolidated = await DbContext
            .Set<FinancialFact>()
            .SingleAsync(f => f.DocumentId == document.Id && f.DimensionsKey == "");
        consolidated.FiscalYear = 2025;
        consolidated.FiscalPeriod = SecFiscalPeriod.FullYear;
        consolidated.Value = 999m;
        consolidated.DocumentId = null;
        await DbContext.SaveChangesAsync();

        await sut.Extract(document, CancellationToken.None);
        await DbContext.Entry(consolidated).ReloadAsync();

        consolidated.FiscalYear.Should().Be(2025);
        consolidated.FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
        consolidated.Value.Should().Be(999m);
        consolidated.DocumentId.Should().BeNull();
    }

    private async Task<Document> SeedDocument(string envelope)
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
            FiscalYearEndMonth = 12,
            FiscalYearEndDay = 31,
        };

        var compressed = GzipCompressor.Compress(System.Text.Encoding.UTF8.GetBytes(envelope));
        var xbrlFile = new File
        {
            Name = "xbrl-envelope",
            Extension = "gz",
            ContentType = "application/gzip",
            Size = compressed.Length,
            FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = compressed },
        };
        var contentFile = new File
        {
            Name = "primary-doc",
            Extension = "txt",
            ContentType = "text/plain",
            Size = 1,
            FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = [0x20] },
        };

        var document = new Document
        {
            CommonStock = stock,
            Content = contentFile,
            DocumentType = DocumentType.TenQ,
            ReportingDate = new DateOnly(2025, 5, 1),
            ReportingForDate = new DateOnly(2025, 3, 31),
            AccessionNumber = Accession,
            XbrlStatus = XbrlCaptureStatus.Captured,
            XbrlType = XbrlType.InlineIxbrl,
            XbrlContent = xbrlFile,
            XbrlUncompressedSize = envelope.Length,
        };

        DbContext.Add(document);
        await DbContext.SaveChangesAsync(CancellationToken.None);
        return document;
    }

    // One dimensional fact (iPhone product cut) and one consolidated fact on
    // the same concept/period — only the former may be persisted.
    private static string InlineEnvelope() =>
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:srt=\"http://fasb.org/srt/2024\" "
        + "xmlns:aapl=\"http://www.apple.com/20250329\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2024\">"
        + "<body><div style=\"display:none\"><ix:header><ix:resources>"
        + "<xbrli:context id=\"Consolidated\">"
        + "<xbrli:entity><xbrli:identifier scheme=\"cik\">0000320193</xbrli:identifier></xbrli:entity>"
        + "<xbrli:period><xbrli:startDate>2025-01-01</xbrli:startDate><xbrli:endDate>2025-03-31</xbrli:endDate></xbrli:period>"
        + "</xbrli:context>"
        + "<xbrli:context id=\"IPhone\">"
        + "<xbrli:entity><xbrli:identifier scheme=\"cik\">0000320193</xbrli:identifier>"
        + "<xbrli:segment><xbrldi:explicitMember dimension=\"srt:ProductOrServiceAxis\">aapl:IPhoneMember</xbrldi:explicitMember></xbrli:segment>"
        + "</xbrli:entity>"
        + "<xbrli:period><xbrli:startDate>2025-01-01</xbrli:startDate><xbrli:endDate>2025-03-31</xbrli:endDate></xbrli:period>"
        + "</xbrli:context>"
        + "<xbrli:unit id=\"usd\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
        + "</ix:resources></ix:header></div>"
        + "<ix:nonFraction name=\"us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax\" "
        + "contextRef=\"Consolidated\" unitRef=\"usd\" decimals=\"-6\" scale=\"6\">95,359</ix:nonFraction>"
        + "<ix:nonFraction name=\"us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax\" "
        + "contextRef=\"IPhone\" unitRef=\"usd\" decimals=\"-6\" scale=\"6\">46,222</ix:nonFraction>"
        + "</body></html>";
}
