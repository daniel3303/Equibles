using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="DocumentRepository.Exists"/>: the composite-key uniqueness
/// check the SEC scraper calls to dedupe filings before persisting. All four
/// predicates (CommonStock, DocumentType, ReportingDate, ReportingForDate) must
/// be load-bearing — a regression that dropped one would let the scraper write
/// duplicate documents on every cycle, exploding the docs table.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentRepositoryExistsTests : ParadeDbMcpTestBase
{
    public DocumentRepositoryExistsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Exists_ChangingOnlyReportingForDate_FlipsFromTrueToFalse()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var file = new File
        {
            Name = "10k", Extension = "htm", ContentType = "text/html", Size = 1,
            FileContent = new FileContent { Bytes = new byte[] { 0x01 } },
        };
        var seeded = new Document
        {
            CommonStock = stock,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 1, 15),
            ReportingForDate = new DateOnly(2024, 9, 30),
        };
        DbContext.Add(stock);
        DbContext.Add(file);
        DbContext.Add(seeded);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        // Re-load the tracked stock so reference equality matches the seeded row.
        var trackedStock = verify.Set<CommonStock>().Single(s => s.Ticker == "AAPL");
        var sut = new DocumentRepository(verify);

        var exactMatch = await sut.Exists(
            trackedStock,
            DocumentType.TenK,
            reportingDate: new DateOnly(2025, 1, 15),
            reportingForDate: new DateOnly(2024, 9, 30)
        );
        // Same composite key except ReportingForDate flipped to a quarter that
        // doesn't match — must come back false. A regression that dropped the
        // ReportingForDate predicate from the AnyAsync expression would mistakenly
        // mark this row as "already present" and skip persisting it.
        var differentReportingFor = await sut.Exists(
            trackedStock,
            DocumentType.TenK,
            reportingDate: new DateOnly(2025, 1, 15),
            reportingForDate: new DateOnly(2024, 6, 30)
        );

        exactMatch.Should().BeTrue();
        differentReportingFor.Should().BeFalse();
    }
}
