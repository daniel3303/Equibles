using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class FiscalCalendarEvidenceReaderTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false, "uncaptured")]
    [InlineData(false, false, "standalone")]
    [InlineData(false, false, "missing-content")]
    [InlineData(false, false, "unknown-size")]
    [InlineData(false, false, "oversized")]
    [InlineData(false, false, "missing-calendar")]
    [InlineData(false, false, "wrong-issuer")]
    [InlineData(false, false, "wrong-period")]
    [InlineData(false, false, "conflicting-calendar")]
    [InlineData(false, false, null, 20)]
    [InlineData(false, false, null, 0, true)]
    public async Task HistoricalGapRetainsItsCalendarAfterNewCalendarAnnualArrives(
        bool secondaryCik,
        bool unknownCurrent,
        string unavailable = null,
        int extraGaps = 0,
        bool standalone = false
    )
    {
        var stock = new CommonStock
        {
            Ticker = "JHG",
            Name = "Calendar transition",
            Cik = "0001274173",
            FiscalYearEndMonth = unknownCurrent ? null : 6,
            FiscalYearEndDay = unknownCurrent ? null : 30,
        };
        if (secondaryCik)
            stock.SecondaryCiks = ["0000001234"];
        File NewFile() =>
            new()
            {
                Name = "source",
                Extension = "txt",
                ContentType = "text/plain",
            };
        var oldEnd = new DateOnly(2025, 12, 31);
        var newEnd = new DateOnly(2027, 6, 30);
        var quarterEnd = new DateOnly(2026, 3, 31);
        Document Annual(DateOnly end) =>
            new()
            {
                CommonStock = stock,
                Content = NewFile(),
                DocumentType = DocumentType.TenK,
                ReportingForDate = end,
                ReportingDate = end.AddDays(30),
                AccessionNumber = end.ToString("yyyyMMdd"),
            };
        DbContext.AddRange(Annual(oldEnd), Annual(newEnd));
        var cik = secondaryCik ? "0000001234" : stock.Cik;
        var envelope = $$"""
            <html xmlns:dei="http://xbrl.sec.gov/dei/2026"><body>
            <xbrli:context id="q"><xbrli:entity><xbrli:identifier scheme="http://www.sec.gov/CIK">{{cik}}</xbrli:identifier></xbrli:entity>
            <xbrli:period><xbrli:startDate>2026-01-01</xbrli:startDate><xbrli:endDate>2026-03-31</xbrli:endDate></xbrli:period></xbrli:context>
            <ix:nonNumeric name="dei:CurrentFiscalYearEndDate" contextRef="q">--12-31</ix:nonNumeric>
            </body></html>
            """;
        if (standalone)
            envelope = $$"""
                <XBRL>
                <?xml version="1.0"?>
                <xbrl xmlns="http://www.xbrl.org/2003/instance" xmlns:dei="http://xbrl.sec.gov/dei/2026">
                <context id="q"><entity><identifier scheme="http://www.sec.gov/CIK">{{cik}}</identifier></entity>
                <period><startDate>2026-01-01</startDate><endDate>2026-03-31</endDate></period></context>
                <dei:CurrentFiscalYearEndDate contextRef="q">--12-31</dei:CurrentFiscalYearEndDate>
                </xbrl>
                </XBRL>
                """;
        var completeEnvelope = envelope;
        envelope = unavailable switch
        {
            "missing-calendar" => envelope.Replace("dei:CurrentFiscalYearEndDate", "dei:Other"),
            "wrong-issuer" => envelope.Replace(cik, "9999999999"),
            "wrong-period" => envelope.Replace("2026-03-31", "2026-03-30"),
            "conflicting-calendar" => envelope.Replace(
                "</body>",
                "<ix:nonNumeric name=\"dei:CurrentFiscalYearEndDate\" contextRef=\"q\">--06-30</ix:nonNumeric></body>"
            ),
            _ => envelope,
        };
        var bytes = Encoding.UTF8.GetBytes(envelope);
        var quarter = new Document
        {
            CommonStock = stock,
            Content = NewFile(),
            DocumentType = DocumentType.TenQ,
            ReportingForDate = quarterEnd,
            ReportingDate = quarterEnd.AddDays(30),
            AccessionNumber = "quarter",
            XbrlStatus =
                unknownCurrent || unavailable == "uncaptured"
                    ? XbrlCaptureStatus.NotChecked
                    : XbrlCaptureStatus.Captured,
            XbrlType =
                unavailable == "standalone" || standalone
                    ? XbrlType.StandaloneXbrl
                    : XbrlType.InlineIxbrl,
            XbrlContent = unavailable == "missing-content" ? null : NewFile(),
            XbrlUncompressedSize =
                unavailable == "unknown-size" ? null
                : unavailable == "oversized" ? 51 * 1024 * 1024
                : bytes.Length,
        };
        DbContext.Add(quarter);
        for (var index = 0; index < extraGaps; index++)
            DbContext.Add(Annual(new DateOnly(2000, 12, 31).AddYears(index)));
        await DbContext.SaveChangesAsync();
        var files = Substitute.For<IFileManager>();
        files.GetContent(Arg.Any<File>()).Returns(GzipCompressor.Compress(bytes));
        var scopes = ServiceScopeSubstitute.Create((typeof(EquiblesFinancialDbContext), DbContext));
        var sut = new FiscalCalendarEvidenceReader(scopes, files, new InlineXbrlParser());
        var calendar = await sut.Read(
            stock,
            [(new(2025, 1, 1), oldEnd), (new(2026, 7, 1), newEnd)],
            CancellationToken.None
        );
        if (unknownCurrent)
        {
            calendar.RequiresHistoricalEvidence.Should().BeFalse();
            calendar.RefusesCalendar(new(2026, 1, 1), quarterEnd).Should().BeFalse();
            await files.DidNotReceive().GetContent(Arg.Any<File>());
        }
        else if (unavailable != null)
        {
            calendar.RequiresHistoricalEvidence.Should().BeTrue();
            calendar.RefusesCalendar(new(2026, 1, 1), quarterEnd).Should().BeTrue();
            calendar.Resolve(new(2026, 1, 1), quarterEnd).Should().BeNull();
            calendar.Resolve(new(2025, 1, 1), oldEnd).Should().Be((2025, SecFiscalPeriod.FullYear));
            calendar.RefusesCalendar(new(2025, 1, 1), oldEnd).Should().BeFalse();

            quarter.XbrlStatus = XbrlCaptureStatus.Captured;
            quarter.XbrlType = XbrlType.InlineIxbrl;
            if (quarter.XbrlContent == null)
            {
                quarter.XbrlContent = NewFile();
                DbContext.Add(quarter.XbrlContent);
            }
            var completeBytes = Encoding.UTF8.GetBytes(completeEnvelope);
            quarter.XbrlUncompressedSize = completeBytes.Length;
            await DbContext.SaveChangesAsync();
            files.GetContent(Arg.Any<File>()).Returns(GzipCompressor.Compress(completeBytes));
            var recovered = await sut.Read(
                stock,
                [(new(2025, 1, 1), oldEnd), (new(2026, 7, 1), newEnd)],
                CancellationToken.None
            );
            recovered.Fingerprint.Should().NotBe(calendar.Fingerprint);
            recovered.Resolve(new(2026, 1, 1), quarterEnd).Should().Be((2026, SecFiscalPeriod.Q1));
        }
        else
        {
            calendar.Resolve(new(2026, 1, 1), quarterEnd).Should().Be((2026, SecFiscalPeriod.Q1));
            calendar.Resolve(quarterEnd, quarterEnd).Should().Be((2026, SecFiscalPeriod.Q1));
        }
    }

    [Fact]
    public async Task EvidenceCheckpointRearmsCompletedAndExhaustedXbrlDocuments()
    {
        var stock = new CommonStock
        {
            Ticker = "REPLAY",
            Name = "Replay",
            Cik = "0000000011",
        };
        var checkpoint = new FinancialFactsSyncStatus
        {
            CommonStock = stock,
            CalendarEvidenceFingerprint = new string('a', 64),
            LastCheckedAt = DateTime.UtcNow,
        };
        var document = new Document
        {
            CommonStock = stock,
            Content = new File
            {
                Name = "filing",
                Extension = "txt",
                ContentType = "text/plain",
            },
            DocumentType = DocumentType.TenQ,
            AccessionNumber = "replay",
            XbrlStatus = XbrlCaptureStatus.Captured,
            XbrlFactsVersion = XbrlFactExtractionService.CurrentVersion,
            XbrlCalendarEvidenceFingerprint = checkpoint.CalendarEvidenceFingerprint,
            XbrlFactsAttempts = Document.MaxXbrlFactsAttempts,
        };
        DbContext.AddRange(checkpoint, document);
        await DbContext.SaveChangesAsync();
        IQueryable<Document> Due() =>
            XbrlFactsExtractionWorker.SelectDueDocuments(
                DbContext.Set<Document>(),
                DbContext.Set<FinancialFactsSyncStatus>()
            );
        (await Due().CountAsync()).Should().Be(0);
        checkpoint.CalendarEvidenceFingerprint = new string('b', 64);
        await DbContext.SaveChangesAsync();
        (await Due().SingleAsync()).Id.Should().Be(document.Id);
        document.XbrlCalendarEvidenceFingerprint = checkpoint.CalendarEvidenceFingerprint;
        document.XbrlFactsVersion = 0;
        document.XbrlFactsAttempts = 1;
        await DbContext.SaveChangesAsync();
        (await Due().CountAsync()).Should().Be(1);
        document.XbrlFactsAttempts = Document.MaxXbrlFactsAttempts;
        await DbContext.SaveChangesAsync();
        (await Due().CountAsync()).Should().Be(0);
    }
}
