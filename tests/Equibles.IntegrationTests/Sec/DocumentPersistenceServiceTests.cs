using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

public class DocumentPersistenceServiceTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;

    public DocumentPersistenceServiceTests()
    {
        // Production DocumentPersistenceService.Save opens an EF Core transaction via
        // BaseRepository.CreateTransaction. The InMemory provider raises
        // InMemoryEventId.TransactionIgnoredWarning by default — and the *default*
        // warning behaviour is Throw, which would surface as InvalidOperationException
        // before the test could exercise the persistence logic itself. Suppress it
        // locally (i.e. NOT in the shared TestDbContextFactory — that would silently
        // hide transaction misuse from every other test that happens not to take this
        // code path today).
        var options = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new EquiblesDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecTestModuleConfiguration(),
            }
        );
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task Save_MultiLineContent_PersistsDocumentWithLineCountFromUtf8SplitOnLf()
    {
        // DocumentPersistenceService.Save derives LineCount with the formula
        //   Encoding.UTF8.GetString(content).Split('\n').Length
        // and persists that value on the Document. Downstream consumers — most visibly
        // the SEC `ReadDocumentLines` MCP tool — paginate by `LineCount`, so a regression
        // in this formula silently breaks every line-based query against SEC filings.
        //
        // Three concrete things this test pins:
        //   (1) Split is on '\n' specifically, not Environment.NewLine — so CRLF-terminated
        //       SEC filings count the same number of "lines" on Windows and Linux runners.
        //   (2) `.Length` (not `.Length - 1`) — a content body terminated with a trailing
        //       '\n' produces an empty last element that IS counted. The test inputs the
        //       three-newline-separated string "First line\nSecond line\nThird line" and
        //       asserts LineCount == 3 (no trailing newline, 3 segments).
        //   (3) The file persisted to IFileManager is the SAME object stored on
        //       Document.Content — wiring this assignment wrong would save the bytes but
        //       point the Document at a different / null file.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            SecondaryTickers = [],
        };
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var savedFile = new File
        {
            Id = Guid.NewGuid(),
            Name = "filing-10k",
            Extension = "html",
            ContentType = "text/html",
            Size = 42,
            FileContent = new FileContent { Bytes = [] },
        };
        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .SaveFile(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(savedFile);

        var documentRepo = new DocumentRepository(_dbContext);
        var sut = new DocumentPersistenceService(documentRepo, fileManager);

        var content = Encoding.UTF8.GetBytes("First line\nSecond line\nThird line");

        await sut.Save(
            company: stock,
            content: content,
            fileName: "filing-10k.html",
            documentType: DocumentType.TenK,
            reportingDate: new DateOnly(2025, 3, 15),
            reportingForDate: new DateOnly(2024, 12, 31),
            sourceUrl: "https://sec.gov/example"
        );

        var persisted = await _dbContext.Set<Document>().SingleAsync();
        persisted.LineCount.Should().Be(3);
        persisted.Content.Should().BeSameAs(savedFile);
        persisted.DocumentType.Should().Be(DocumentType.TenK);
        persisted.ReportingDate.Should().Be(new DateOnly(2025, 3, 15));
        persisted.ReportingForDate.Should().Be(new DateOnly(2024, 12, 31));
        persisted.SourceUrl.Should().Be("https://sec.gov/example");
    }
}
