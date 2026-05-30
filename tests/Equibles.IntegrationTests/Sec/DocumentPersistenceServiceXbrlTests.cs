using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Exercises <see cref="DocumentPersistenceService.Save"/>'s raw-XBRL capture branch against
/// real Postgres: the captured envelope must be gzip-compressed into an internal
/// <see cref="File"/> (bypassing the upload allowlist), linked from the document via the new
/// nullable FK, and the status/type/uncompressed-size recorded. Absent/not-checked results
/// must only set the status and leave the document with no XBRL file.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentPersistenceServiceXbrlTests : ParadeDbMcpTestBase
{
    public DocumentPersistenceServiceXbrlTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private const string XbrlBody =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><ix:nonFraction>42</ix:nonFraction></body></html>";

    private async Task<CommonStock> SeedCompany()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        await using (var seed = Fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(apple);
            await seed.SaveChangesAsync();
        }
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<CommonStock>().SingleAsync(s => s.Id == apple.Id);
    }

    private DocumentPersistenceService BuildSut() =>
        new(new DocumentRepository(DbContext), new FileManager(new FileRepository(DbContext)));

    [Fact]
    public async Task Save_WithCapturedInlineXbrl_StoresGzipFileAndRecordsXbrlFields()
    {
        var apple = await SeedCompany();
        var rawBytes = Encoding.UTF8.GetBytes(XbrlBody);

        await BuildSut()
            .Save(
                company: apple,
                content: "# markdown"u8.ToArray(),
                fileName: "AAPL-2024-10K.txt",
                documentType: DocumentType.TenK,
                reportingDate: new DateOnly(2024, 3, 15),
                reportingForDate: new DateOnly(2023, 12, 31),
                sourceUrl: "https://example.test/filing",
                accessionNumber: "0000320193-24-000123",
                xbrl: XbrlCaptureResult.Captured(XbrlType.InlineIxbrl, "aapl-10k.htm", rawBytes)
            );

        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.CommonStockId == apple.Id);

        saved.XbrlStatus.Should().Be(XbrlCaptureStatus.Captured);
        saved.XbrlType.Should().Be(XbrlType.InlineIxbrl);
        saved.XbrlUncompressedSize.Should().Be(rawBytes.Length);
        saved.XbrlContentId.Should().NotBeNull();

        var xbrlFile = await verify.Set<File>().SingleAsync(f => f.Id == saved.XbrlContentId);
        xbrlFile.ContentType.Should().Be("application/gzip");
        xbrlFile.Extension.Should().Be("gz");
        xbrlFile.Size.Should().Be(xbrlFile.FileContent.Bytes.Length);
        Gunzip(xbrlFile.FileContent.Bytes).Should().Equal(rawBytes);
    }

    [Fact]
    public async Task Save_WithNotPresentXbrl_RecordsStatusWithoutFile()
    {
        var apple = await SeedCompany();

        await BuildSut()
            .Save(
                company: apple,
                content: "# markdown"u8.ToArray(),
                fileName: "AAPL-2024-8K.txt",
                documentType: DocumentType.EightK,
                reportingDate: new DateOnly(2024, 3, 15),
                reportingForDate: new DateOnly(2024, 3, 15),
                sourceUrl: "https://example.test/filing",
                accessionNumber: "0000320193-24-000124",
                xbrl: XbrlCaptureResult.NotPresent
            );

        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.CommonStockId == apple.Id);

        saved.XbrlStatus.Should().Be(XbrlCaptureStatus.NotPresent);
        saved.XbrlContentId.Should().BeNull();
        saved.XbrlType.Should().BeNull();
    }

    private static byte[] Gunzip(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
