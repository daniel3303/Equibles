using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// End-to-end pin of the cover-page 12(b) extraction (#5568): a captured
/// inline envelope's Security12bTitle / TradingSymbol / SecurityExchangeName
/// facts land as ListedSecurity rows, and the row matching the stock's own
/// ticker materializes its ListedSecurityType — so an issuer whose only listed
/// security is a baby bond stops passing for common equity. Also pins the
/// ordering guard (the historical drain visits old filings after new ones, so
/// an older filing's statement must never overwrite a newer row) and symbol
/// normalization end to end (filings write "BRK.B" where the ticker feed says
/// "BRK-B").
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class XbrlFactExtractionServiceCoverListingsTests : ParadeDbMcpTestBase
{
    public XbrlFactExtractionServiceCoverListingsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Extract_CoverWithNoteAndCommonRows_PersistsListingsAndClassifiesTicker()
    {
        // The QVC shape: the stock row's ticker IS the listed baby bond.
        var stock = SeedStock(ticker: "QVCC");
        var document = await SeedDocument(
            stock,
            CoverEnvelope(
                ("Common", "Common Stock, par value $0.01 per share", "QVCGA", "NASDAQ"),
                ("Notes68", "6.875% Senior Secured Notes due 2068", "QVCC", "NASDAQ")
            ),
            accession: "0001355096-26-000010",
            reportingDate: new DateOnly(2026, 5, 15)
        );

        await BuildSut().Extract(document, CancellationToken.None);

        var rows = await DbContext
            .Set<ListedSecurity>()
            .Where(r => r.CommonStockId == stock.Id)
            .OrderBy(r => r.TradingSymbol)
            .ToListAsync(CancellationToken.None);

        rows.Should().HaveCount(2);
        rows[0].TradingSymbol.Should().Be("QVCC");
        rows[0].Title.Should().Be("6.875% Senior Secured Notes due 2068");
        rows[0].ExchangeName.Should().Be("NASDAQ");
        rows[0].AccessionNumber.Should().Be("0001355096-26-000010");
        rows[0].FiledDate.Should().Be(new DateOnly(2026, 5, 15));
        rows[1].TradingSymbol.Should().Be("QVCGA");

        var reloaded = await ReloadStock(stock);
        reloaded.ListedSecurityType.Should().Be(ListedSecurityType.DebtSecurities);
        reloaded.ListedSecurityTitle.Should().Be("6.875% Senior Secured Notes due 2068");
    }

    [Fact]
    public async Task Extract_OlderFiling_NeverOverwritesNewerStatement()
    {
        var stock = SeedStock(ticker: "SOHO");
        var newer = await SeedDocument(
            stock,
            CoverEnvelope(("C1", "Common Stock, par value $0.01", "SOHO", "NASDAQ")),
            accession: "0001301236-26-000020",
            reportingDate: new DateOnly(2026, 4, 15)
        );
        var older = await SeedDocument(
            stock,
            CoverEnvelope(("C1", "An obsolete earlier title", "SOHO", "NYSE")),
            accession: "0001301236-25-000009",
            reportingDate: new DateOnly(2025, 3, 20)
        );

        var sut = BuildSut();
        await sut.Extract(newer, CancellationToken.None);
        await sut.Extract(older, CancellationToken.None);

        var row = (
            await DbContext
                .Set<ListedSecurity>()
                .Where(r => r.CommonStockId == stock.Id)
                .ToListAsync(CancellationToken.None)
        )
            .Should()
            .ContainSingle()
            .Subject;
        row.Title.Should().Be("Common Stock, par value $0.01");
        row.FiledDate.Should().Be(new DateOnly(2026, 4, 15));

        var reloaded = await ReloadStock(stock);
        reloaded.ListedSecurityType.Should().Be(ListedSecurityType.CommonShares);
    }

    [Fact]
    public async Task Extract_FiledDotSymbol_MatchesDashTicker()
    {
        // The ticker feed writes class shares with a dash; the filing uses a
        // dot. Both must normalize onto the same row or the class share never
        // classifies.
        var stock = SeedStock(ticker: "BRK-B");
        var document = await SeedDocument(
            stock,
            CoverEnvelope(("C1", "Class B Common Stock", "BRK.B", "NYSE")),
            accession: "0001067983-26-000005",
            reportingDate: new DateOnly(2026, 2, 25)
        );

        await BuildSut().Extract(document, CancellationToken.None);

        var reloaded = await ReloadStock(stock);
        reloaded.ListedSecurityType.Should().Be(ListedSecurityType.CommonShares);
        reloaded.ListedSecurityTitle.Should().Be("Class B Common Stock");
    }

    [Fact]
    public async Task Extract_EnvelopeWithoutCoverFacts_LeavesClassificationUntouched()
    {
        // Many report types carry no 12(b) table; absence is not evidence.
        var stock = SeedStock(ticker: "ACME");
        var document = await SeedDocument(
            stock,
            CoverEnvelope(),
            accession: "0000000001-26-000001",
            reportingDate: new DateOnly(2026, 6, 1)
        );

        await BuildSut().Extract(document, CancellationToken.None);

        (await DbContext.Set<ListedSecurity>().CountAsync(CancellationToken.None)).Should().Be(0);
        var reloaded = await ReloadStock(stock);
        reloaded.ListedSecurityType.Should().Be(ListedSecurityType.Unknown);
        reloaded.ListedSecurityTitle.Should().BeNull();
    }

    [Fact]
    public async Task Extract_SameDocumentTwice_KeepsOneRowPerSymbol()
    {
        var stock = SeedStock(ticker: "ACME");
        var document = await SeedDocument(
            stock,
            CoverEnvelope(("C1", "Common Stock", "ACME", "NYSE")),
            accession: "0000000001-26-000002",
            reportingDate: new DateOnly(2026, 6, 1)
        );

        var sut = BuildSut();
        await sut.Extract(document, CancellationToken.None);
        await sut.Extract(document, CancellationToken.None);

        (
            await DbContext
                .Set<ListedSecurity>()
                .CountAsync(r => r.CommonStockId == stock.Id, CancellationToken.None)
        )
            .Should()
            .Be(1);
    }

    private XbrlFactExtractionService BuildSut()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (typeof(FinancialConceptRepository), new FinancialConceptRepository(DbContext)),
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (typeof(ListedSecurityRepository), new ListedSecurityRepository(DbContext))
        );
        var fileManager = Substitute.For<IFileManager>();
        fileManager.GetContent(Arg.Any<File>()).Returns(ci => ((File)ci[0]).FileContent.Bytes);
        return new XbrlFactExtractionService(
            scopeFactory,
            new InlineXbrlParser(),
            new StandaloneXbrlParser(),
            fileManager,
            NullLogger<XbrlFactExtractionService>()
        );
    }

    private CommonStock SeedStock(string ticker)
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Corp.",
            Cik = ticker,
        };
        DbContext.Add(stock);
        return stock;
    }

    private async Task<CommonStock> ReloadStock(CommonStock stock)
    {
        return await DbContext
            .Set<CommonStock>()
            .AsNoTracking()
            .FirstAsync(s => s.Id == stock.Id, CancellationToken.None);
    }

    private async Task<Document> SeedDocument(
        CommonStock stock,
        string envelope,
        string accession,
        DateOnly reportingDate
    )
    {
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
            ReportingDate = reportingDate,
            ReportingForDate = reportingDate.AddMonths(-1),
            AccessionNumber = accession,
            XbrlStatus = XbrlCaptureStatus.Captured,
            XbrlType = XbrlType.InlineIxbrl,
            XbrlContent = xbrlFile,
            XbrlUncompressedSize = envelope.Length,
        };

        DbContext.Add(document);
        await DbContext.SaveChangesAsync(CancellationToken.None);
        return document;
    }

    // A minimal inline envelope whose only iXBRL content is the cover's 12(b)
    // rows — one context per registered security.
    private static string CoverEnvelope(
        params (string Context, string Title, string Symbol, string Exchange)[] listings
    )
    {
        var body = string.Concat(
            listings.Select(l =>
                $"<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"{l.Context}\">{l.Title}</ix:nonNumeric>"
                + $"<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"{l.Context}\">{l.Symbol}</ix:nonNumeric>"
                + $"<ix:nonNumeric name=\"dei:SecurityExchangeName\" contextRef=\"{l.Context}\">{l.Exchange}</ix:nonNumeric>"
            )
        );
        return "<html xmlns=\"http://www.w3.org/1999/xhtml\" "
            + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:dei=\"http://xbrl.sec.gov/dei/2025\">"
            + "<body>"
            + body
            + "</body></html>";
    }
}
