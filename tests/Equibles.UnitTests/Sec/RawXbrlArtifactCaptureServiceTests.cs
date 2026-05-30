using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class RawXbrlArtifactCaptureServiceTests
{
    private readonly ISecEdgarClient _secEdgarClient = Substitute.For<ISecEdgarClient>();

    private static EquiblesFinancialDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new RawFilingArtifactOnlyModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private RawXbrlArtifactCaptureService BuildService(
        EquiblesFinancialDbContext dbContext,
        RawXbrlArtifactOptions options
    )
    {
        return new RawXbrlArtifactCaptureService(
            _secEdgarClient,
            new RawFilingArtifactRepository(dbContext),
            Options.Create(options),
            Substitute.For<ILogger<RawXbrlArtifactCaptureService>>()
        );
    }

    private static CommonStock SeedStock(EquiblesFinancialDbContext db)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        db.Set<CommonStock>().Add(stock);
        db.SaveChanges();
        return stock;
    }

    private static FilingData Filing(string accession = "0000320193-18-000145")
    {
        return new FilingData
        {
            Cik = "0000320193",
            AccessionNumber = accession,
            FilingDate = new DateOnly(2018, 11, 5),
            Form = "10-K",
            PrimaryDocument = "aapl-20180929.htm",
        };
    }

    private const string InlineXbrlHtml =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><ix:nonFraction>1</ix:nonFraction></body></html>";

    private void StubPrimaryDocument(FilingData filing, string body)
    {
        _secEdgarClient
            .GetDocumentFileBytes(filing.Cik, filing.AccessionNumber, filing.PrimaryDocument)
            .Returns(Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public async Task Capture_Disabled_StoresNothingAndDoesNotCallClient()
    {
        using var db = NewDbContext();
        var stock = SeedStock(db);
        var filing = Filing();
        StubPrimaryDocument(filing, InlineXbrlHtml);
        var service = BuildService(
            db,
            new RawXbrlArtifactOptions { Enabled = false, CaptureInlineIxbrl = true }
        );

        await service.Capture(stock, filing);

        db.Set<RawFilingArtifact>().Should().BeEmpty();
        await _secEdgarClient
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Capture_InlineEnabled_StoresGzippedPrimaryDocumentWithSizes()
    {
        using var db = NewDbContext();
        var stock = SeedStock(db);
        var filing = Filing();
        StubPrimaryDocument(filing, InlineXbrlHtml);
        var service = BuildService(
            db,
            new RawXbrlArtifactOptions { Enabled = true, CaptureInlineIxbrl = true }
        );

        await service.Capture(stock, filing);

        var stored = db.Set<RawFilingArtifact>().Single();
        stored.ArtifactType.Should().Be(RawFilingArtifactType.InlineIxbrl);
        stored.SourceFileName.Should().Be("aapl-20180929.htm");
        stored.UncompressedSize.Should().Be(Encoding.UTF8.GetByteCount(InlineXbrlHtml));
        stored.CompressedSize.Should().Be(stored.Content.Length);
        Decompress(stored.Content).Should().Be(InlineXbrlHtml);
    }

    [Fact]
    public async Task Capture_InlinePrimaryDocumentWithoutXbrlMarkers_StoresNothing()
    {
        using var db = NewDbContext();
        var stock = SeedStock(db);
        var filing = Filing();
        StubPrimaryDocument(filing, "<html><body>no inline xbrl here</body></html>");
        var service = BuildService(
            db,
            new RawXbrlArtifactOptions { Enabled = true, CaptureInlineIxbrl = true }
        );

        await service.Capture(stock, filing);

        db.Set<RawFilingArtifact>().Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_InlineAlreadyExists_DoesNotFetchOrStoreDuplicate()
    {
        using var db = NewDbContext();
        var stock = SeedStock(db);
        var filing = Filing();
        StubPrimaryDocument(filing, InlineXbrlHtml);
        db.Set<RawFilingArtifact>()
            .Add(
                new RawFilingArtifact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    AccessionNumber = filing.AccessionNumber,
                    ArtifactType = RawFilingArtifactType.InlineIxbrl,
                    SourceFileName = "old.htm",
                    Content = [9, 9, 9],
                    UncompressedSize = 3,
                    CompressedSize = 3,
                }
            );
        await db.SaveChangesAsync();
        var service = BuildService(
            db,
            new RawXbrlArtifactOptions { Enabled = true, CaptureInlineIxbrl = true }
        );

        await service.Capture(stock, filing);

        db.Set<RawFilingArtifact>().Should().ContainSingle();
        db.Set<RawFilingArtifact>().Single().SourceFileName.Should().Be("old.htm");
        await _secEdgarClient
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Capture_StandaloneEnabled_FetchesInstanceAndStoresGzip()
    {
        using var db = NewDbContext();
        var stock = SeedStock(db);
        var filing = Filing();
        var instanceBytes = Encoding.UTF8.GetBytes("<xbrl>standalone instance</xbrl>");
        _secEdgarClient
            .GetFilingArtifactNames(filing.Cik, filing.AccessionNumber)
            .Returns(["aapl-20180929_cal.xml", "aapl-20180929.xml", "R1.xml"]);
        _secEdgarClient
            .GetDocumentFileBytes(filing.Cik, filing.AccessionNumber, "aapl-20180929.xml")
            .Returns(instanceBytes);
        var service = BuildService(
            db,
            new RawXbrlArtifactOptions { Enabled = true, CaptureStandaloneXbrl = true }
        );

        await service.Capture(stock, filing);

        var stored = db.Set<RawFilingArtifact>().Single();
        stored.ArtifactType.Should().Be(RawFilingArtifactType.StandaloneXbrl);
        stored.SourceFileName.Should().Be("aapl-20180929.xml");
        Decompress(stored.Content).Should().Be("<xbrl>standalone instance</xbrl>");
    }

    [Fact]
    public async Task Capture_StandaloneEnabled_NoInstanceInArtifactList_StoresNothing()
    {
        using var db = NewDbContext();
        var stock = SeedStock(db);
        var filing = Filing();
        _secEdgarClient
            .GetFilingArtifactNames(filing.Cik, filing.AccessionNumber)
            .Returns(["aapl-20180929_cal.xml", "aapl-20180929_lab.xml", "R1.xml", "primary.htm"]);
        var service = BuildService(
            db,
            new RawXbrlArtifactOptions { Enabled = true, CaptureStandaloneXbrl = true }
        );

        await service.Capture(stock, filing);

        db.Set<RawFilingArtifact>().Should().BeEmpty();
        await _secEdgarClient
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void SelectStandaloneXbrlInstance_ExcludesLinkbasesSummaryAndRenderedReports()
    {
        var names = new[]
        {
            "FilingSummary.xml",
            "aapl-20180929_cal.xml",
            "aapl-20180929_def.xml",
            "aapl-20180929_lab.xml",
            "aapl-20180929_pre.xml",
            "R1.xml",
            "R42.xml",
            "aapl-20180929.xsd",
            "aapl-20180929.xml",
        };

        var selected = RawXbrlArtifactCaptureService.SelectStandaloneXbrlInstance(names);

        selected.Should().Be("aapl-20180929.xml");
    }

    [Fact]
    public void SelectStandaloneXbrlInstance_NoInstancePresent_ReturnsNull()
    {
        var names = new[] { "aapl-20180929_cal.xml", "R1.xml", "primary.htm" };

        RawXbrlArtifactCaptureService.SelectStandaloneXbrlInstance(names).Should().BeNull();
    }

    private static string Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
