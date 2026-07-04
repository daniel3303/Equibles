using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

// DocumentRepository.Exists is the document sync's dedup gate. Two DISTINCT
// filings of the same form can share (filing date, report date) — e.g. two 8-Ks
// filed the same day for the same period — so when the caller supplies the
// filing's accession number the gate must dedup on it, while rows ingested
// before the accession was stamped (null) must still match on the legacy
// 4-field key so history is never re-ingested as duplicates.
public class DocumentRepositoryExistsAccessionTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly DocumentRepository _repository;
    private readonly CommonStock _company;
    private static readonly DateOnly FilingDate = new(2025, 3, 10);
    private static readonly DateOnly ReportDate = new(2025, 3, 10);

    public DocumentRepositoryExistsAccessionTests()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        _dbContext = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecTestModuleConfiguration(),
            }
        );
        _dbContext.Database.EnsureCreated();
        _repository = new DocumentRepository(_dbContext);

        _company = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        _dbContext.Add(_company);
        _dbContext.SaveChanges();
    }

    public void Dispose() => _dbContext.Dispose();

    private void SeedDocument(string accessionNumber)
    {
        var file = new File
        {
            Id = Guid.NewGuid(),
            Name = "doc",
            Extension = "txt",
            ContentType = "text/plain",
        };
        _dbContext.Add(file);
        _dbContext.Add(
            new Document
            {
                CommonStock = _company,
                Content = file,
                DocumentType = DocumentType.EightK,
                ReportingDate = FilingDate,
                ReportingForDate = ReportDate,
                AccessionNumber = accessionNumber,
            }
        );
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Exists_SameAccession_ReturnsTrue()
    {
        SeedDocument("0000320193-25-000001");

        var exists = await _repository.Exists(
            _company,
            DocumentType.EightK,
            FilingDate,
            ReportDate,
            "0000320193-25-000001"
        );

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Exists_SameDayKeyButDifferentAccession_ReturnsFalse()
    {
        // The second same-day filing of the same form is a distinct filing —
        // the old 4-field key silently dropped it forever.
        SeedDocument("0000320193-25-000001");

        var exists = await _repository.Exists(
            _company,
            DocumentType.EightK,
            FilingDate,
            ReportDate,
            "0000320193-25-000002"
        );

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Exists_LegacyRowWithoutAccession_MatchesOnFourFieldKey()
    {
        // ~94k prod rows predate accession stamping; they must keep deduping
        // on the legacy key or the sync would re-ingest them all as duplicates.
        SeedDocument(accessionNumber: null);

        var exists = await _repository.Exists(
            _company,
            DocumentType.EightK,
            FilingDate,
            ReportDate,
            "0000320193-25-000001"
        );

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Exists_NoAccessionSupplied_UsesFourFieldKey()
    {
        SeedDocument("0000320193-25-000001");

        var exists = await _repository.Exists(
            _company,
            DocumentType.EightK,
            FilingDate,
            ReportDate
        );

        exists.Should().BeTrue();
    }
}
