using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

// Fund reports stored with no period (Postgres -infinity) are dated from their cover; every other row is left alone.
[Collection(ParadeDbCollection.Name)]
public class FundReportPeriodBackfillServiceTests : ParadeDbMcpTestBase
{
    private const string StatedCover =
        "Date of fiscal year end: 12/31/2022\n\nDate of reporting period: 06/30/2022\n\nItem 1 – Report to Stockholders";

    private readonly Dictionary<Guid, byte[]> _stored = [];

    public FundReportPeriodBackfillServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private async Task<Guid> Seed(
        DocumentType documentType,
        DateOnly reportingForDate,
        string text,
        string accessionNumber
    )
    {
        await using var seed = Fixture.CreateDbContext();
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: null,
            Name: "Fund Trust",
            Cik: Random.Shared.Next(1_000_000, 9_999_999).ToString("D10")
        );
        seed.Set<EquityIssuer>().Add(issuer);
        var content = new File
        {
            Id = Guid.NewGuid(),
            Name = "content",
            Extension = "txt",
            ContentType = "text/plain",
            Size = text.Length,
            FileContent = new() { Bytes = Encoding.UTF8.GetBytes(text) },
        };
        seed.Set<File>().Add(content);
        _stored[content.Id] = Encoding.UTF8.GetBytes(text);
        var document = new Document
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            Content = content,
            DocumentType = documentType,
            ReportingDate = new DateOnly(2022, 9, 1),
            ReportingForDate = reportingForDate,
            AccessionNumber = accessionNumber,
        };
        seed.Set<Document>().Add(document);
        await seed.SaveChangesAsync();
        return document.Id;
    }

    private FundReportPeriodBackfillService Service()
    {
        var files = Substitute.For<IFileManager>();
        files
            .GetContent(Arg.Any<File>())
            .Returns(call =>
                _stored.TryGetValue(call.Arg<File>().Id, out var bytes)
                    ? bytes
                    : throw new FileNotFoundException("stored text is missing")
            );
        return new FundReportPeriodBackfillService(
            new DocumentRepository(DbContext),
            files,
            NullLogger<FundReportPeriodBackfillService>()
        );
    }

    private async Task<DateOnly> Period(Guid id)
    {
        await using var verify = Fixture.CreateDbContext();
        return (await verify.Set<Document>().SingleAsync(d => d.Id == id)).ReportingForDate;
    }

    [Fact]
    public async Task Backfill_UndatedFundReports_AreDatedFromTheirCover()
    {
        var stated = await Seed(
            DocumentType.NCsrs,
            DateOnly.MinValue,
            StatedCover,
            "0001193125-22-236310"
        );
        var silent = await Seed(
            DocumentType.NCsr,
            DateOnly.MinValue,
            "Date of reporting period: December 31\n\nAnnual Report",
            "0001193125-22-236321"
        );
        var dated = await Seed(
            DocumentType.NCsr,
            new DateOnly(2022, 3, 31),
            StatedCover,
            "0001193125-22-236276"
        );
        var withoutAccession = await Seed(DocumentType.NCsr, DateOnly.MinValue, StatedCover, null);
        var proxy = await Seed(
            DocumentType.Def14A,
            DateOnly.MinValue,
            StatedCover,
            "0001193125-22-236179"
        );
        DbContext.ChangeTracker.Clear();

        var result = await Service().Backfill();

        (await Period(stated)).Should().Be(new DateOnly(2022, 6, 30));
        (await Period(silent)).Should().Be(DateOnly.MinValue);
        (await Period(dated)).Should().Be(new DateOnly(2022, 3, 31));
        (await Period(withoutAccession)).Should().Be(DateOnly.MinValue);
        (await Period(proxy)).Should().Be(DateOnly.MinValue);
        (result.Selected, result.Dated).Should().Be((2, 1));
    }

    // A stored file that cannot be read skips its own report; the others are still dated.
    [Fact]
    public async Task Backfill_UnreadableStoredText_SkipsOnlyThatReport()
    {
        var unreadable = await Seed(
            DocumentType.NCsr,
            DateOnly.MinValue,
            StatedCover,
            "0001193125-22-236201"
        );
        var readable = await Seed(
            DocumentType.NCsr,
            DateOnly.MinValue,
            StatedCover,
            "0001193125-22-236250"
        );
        await using (var lookup = Fixture.CreateDbContext())
        {
            var contentId = await lookup
                .Set<Document>()
                .Where(d => d.Id == unreadable)
                .Select(d => d.ContentId)
                .SingleAsync();
            _stored.Remove(contentId);
        }
        DbContext.ChangeTracker.Clear();

        var result = await Service().Backfill();

        (await Period(unreadable)).Should().Be(DateOnly.MinValue);
        (await Period(readable)).Should().Be(new DateOnly(2022, 6, 30));
        (result.Dated, result.Unreadable).Should().Be((1, 1));
    }
}
