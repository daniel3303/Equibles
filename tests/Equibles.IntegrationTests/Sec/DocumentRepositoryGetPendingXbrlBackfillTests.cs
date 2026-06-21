using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="DocumentRepository.GetPendingXbrlBackfill"/>: the backfill work-set the
/// worker actually selects and the dashboard "XBRL backfill pending" stat counts. The guard is
/// that a <see cref="XbrlCaptureStatus.NotChecked"/> document only qualifies when it has a
/// re-fetchable EDGAR filing (accession, or an EDGAR submission URL) and a CIK, and hasn't
/// exhausted its retry ceiling. Without this, non-EDGAR documents (e.g. earnings-call
/// transcripts) — which are NotChecked but have no filing to fetch — inflate the pending count
/// and make the backfill look permanently stuck.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentRepositoryGetPendingXbrlBackfillTests : ParadeDbMcpTestBase
{
    public DocumentRepositoryGetPendingXbrlBackfillTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static File NewContent()
    {
        return new File
        {
            Name = "content",
            Extension = "txt",
            ContentType = "text/plain",
            Size = 4,
            FileContent = new() { Bytes = "body"u8.ToArray() },
        };
    }

    [Fact]
    public async Task GetPendingXbrlBackfill_ReturnsOnlyReFetchableNotCheckedDocuments()
    {
        var withCik = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var withoutCik = new CommonStock { Ticker = "NOCIK", Name = "No Cik Inc." };

        // Qualifies: NotChecked, carries an accession, issuer has a CIK, under the ceiling.
        var eligibleByAccession = new Document
        {
            CommonStock = withCik,
            Content = NewContent(),
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 2, 1),
            AccessionNumber = "0000320193-24-000001",
            XbrlStatus = XbrlCaptureStatus.NotChecked,
        };
        // Qualifies: accession is null but recoverable from the EDGAR submission URL.
        var eligibleByEdgarUrl = new Document
        {
            CommonStock = withCik,
            Content = NewContent(),
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 1, 1),
            SourceUrl = "https://www.sec.gov/Archives/edgar/data/320193/0000320193-24-000002.txt",
            XbrlStatus = XbrlCaptureStatus.NotChecked,
        };
        // Excluded: non-EDGAR document (transcript) — NotChecked but no filing to fetch. This is
        // the row class that was inflating the dashboard's pending count.
        var nonEdgar = new Document
        {
            CommonStock = withCik,
            Content = NewContent(),
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 3, 1),
            SourceUrl = "https://www.alphavantage.co/query?function=EARNINGS_CALL_TRANSCRIPT",
            XbrlStatus = XbrlCaptureStatus.NotChecked,
        };
        // Excluded: issuer has no CIK, so the filing can't be re-fetched.
        var missingCik = new Document
        {
            CommonStock = withoutCik,
            Content = NewContent(),
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 2, 1),
            AccessionNumber = "0000000000-24-000003",
            XbrlStatus = XbrlCaptureStatus.NotChecked,
        };
        // Excluded: exhausted its retry ceiling, so it can no longer be reselected.
        var exhausted = new Document
        {
            CommonStock = withCik,
            Content = NewContent(),
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 2, 1),
            AccessionNumber = "0000320193-24-000004",
            XbrlStatus = XbrlCaptureStatus.NotChecked,
            XbrlCaptureAttempts = Document.MaxXbrlCaptureAttempts,
        };
        // Excluded: already terminal (captured), not pending.
        var captured = new Document
        {
            CommonStock = withCik,
            Content = NewContent(),
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 2, 1),
            AccessionNumber = "0000320193-24-000005",
            XbrlStatus = XbrlCaptureStatus.Captured,
        };

        DbContext.AddRange(
            withCik,
            withoutCik,
            eligibleByAccession,
            eligibleByEdgarUrl,
            nonEdgar,
            missingCik,
            exhausted,
            captured
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new DocumentRepository(verify);

        var pending = await sut.GetPendingXbrlBackfill().AsNoTracking().ToListAsync();

        pending
            .Select(d => d.Id)
            .Should()
            .BeEquivalentTo([eligibleByAccession.Id, eligibleByEdgarUrl.Id]);
    }
}
