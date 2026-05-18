using System.Data;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The sibling in-memory <see cref="DocumentPersistenceServiceTests"/> opens its
/// <see cref="DbContext"/> with <c>InMemoryEventId.TransactionIgnoredWarning</c> suppressed —
/// the EF Core InMemory provider treats <see cref="DbContext.Database.BeginTransactionAsync()"/>
/// as a no-op, so the <c>await using transaction</c> + <c>await transaction.CommitAsync</c>
/// scope in <see cref="DocumentPersistenceService.Save"/> is never exercised. Against real
/// Postgres, the transaction is real: a regression that drops <c>CommitAsync</c> (or
/// throws between <c>SaveChanges</c> and the commit without a rollback) would silently
/// abort the save, and the in-memory tier would not catch it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentPersistenceServiceSaveTests : ParadeDbMcpTestBase
{
    public DocumentPersistenceServiceSaveTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Save_CommitsTransactionAndPersistsDocumentLinkedToCompanyAndContent()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        // FileManager is the unit under test's only non-DbContext collaborator — substitute it
        // so the test is independent of Equibles.Media.BusinessLogic; the persistence step is
        // what we want to verify, not the file-byte writing.
        var savedFile = new File
        {
            Id = Guid.NewGuid(),
            Name = "AAPL-2024-10K",
            Extension = "html",
            ContentType = "text/html",
            Size = 32,
            FileContent = new FileContent
            {
                Bytes = "<html>line1\nline2\nline3</html>"u8.ToArray(),
            },
        };

        // Seed via a separate DbContext to keep these rows out of the SUT context's tracker —
        // the production DocumentPersistenceService passes a CommonStock navigation through
        // `new Document { CommonStock = company }`. If the SUT context tracked apple as
        // Added (from seeding), SaveChanges would try to INSERT it twice; if apple weren't
        // tracked at all, the same navigation would lead EF to treat it as a fresh row and
        // insert it. Seeding through a separate context + fetching apple back via the SUT
        // context lands it as Unchanged.
        await using (var seed = Fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(apple);
            seed.Set<File>().Add(savedFile);
            await seed.SaveChangesAsync();
        }
        DbContext.ChangeTracker.Clear();
        apple = await DbContext.Set<CommonStock>().SingleAsync(s => s.Id == apple.Id);
        // SaveFile in production returns the row it just persisted via the same DbContext;
        // re-fetch from the SUT context so EF tracks it as Unchanged when Save uses it as
        // the Document.Content navigation.
        savedFile = await DbContext.Set<File>().SingleAsync(f => f.Id == savedFile.Id);

        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .SaveFile(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(_ => Task.FromResult(savedFile));

        var sut = new DocumentPersistenceService(new DocumentRepository(DbContext), fileManager);

        var body = "<html>line1\nline2\nline3</html>"u8.ToArray();
        await sut.Save(
            company: apple,
            content: body,
            fileName: "AAPL-2024-10K.html",
            documentType: DocumentType.TenK,
            reportingDate: new DateOnly(2024, 3, 15),
            reportingForDate: new DateOnly(2023, 12, 31),
            sourceUrl: "https://example.test/filing",
            accessionNumber: "0000320193-24-000123"
        );

        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.CommonStockId == apple.Id);

        // Each assertion catches a distinct silent-abort regression: the transaction must
        // commit (saved row exists), the Content FK must be set to the file the FileManager
        // returned (Save replaced the in-memory file with the saved one), and the LineCount
        // must reflect the UTF-8 LF count from the content bytes — drop CommitAsync and the
        // SingleAsync throws; mishandle Content and ContentId stays Guid.Empty; mis-encode
        // and LineCount drifts.
        saved.ContentId.Should().Be(savedFile.Id);
        saved.DocumentType.Should().Be(DocumentType.TenK);
        saved.ReportingDate.Should().Be(new DateOnly(2024, 3, 15));
        saved.LineCount.Should().Be(3, "the LF-split line count for 3 \\n-separated segments is 3");
        saved.SourceUrl.Should().Be("https://example.test/filing");
        saved.AccessionNumber.Should().Be("0000320193-24-000123");
    }
}
