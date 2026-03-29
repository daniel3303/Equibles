using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Tests.Sec;

public class SecRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly DocumentRepository _documentRepo;
    private readonly FailToDeliverRepository _ftdRepo;

    public SecRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration());
        _documentRepo = new DocumentRepository(_dbContext);
        _ftdRepo = new FailToDeliverRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.", List<string> secondaryTickers = null) {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            SecondaryTickers = secondaryTickers ?? [],
        };
    }

    private File CreateFile(string name = "filing", string extension = "html") {
        return new File {
            Id = Guid.NewGuid(),
            Name = name,
            Extension = extension,
            ContentType = $"text/{extension}",
            Size = 1024,
            FileContent = new FileContent { Bytes = [0x01, 0x02] },
        };
    }

    private Document CreateDocument(
        CommonStock stock,
        DocumentType type = null,
        DateOnly? reportingDate = null,
        DateOnly? reportingForDate = null,
        File content = null
    ) {
        content ??= CreateFile();
        return new Document {
            Id = Guid.NewGuid(),
            CommonStock = stock,
            CommonStockId = stock.Id,
            DocumentType = type ?? DocumentType.TenK,
            ReportingDate = reportingDate ?? new DateOnly(2025, 1, 15),
            ReportingForDate = reportingForDate ?? new DateOnly(2024, 12, 31),
            Content = content,
            ContentId = content.Id,
            SourceUrl = "https://sec.gov/example",
            LineCount = 100,
        };
    }

    private async Task<CommonStock> SeedStock(string ticker = "AAPL", string name = "Apple Inc.", List<string> secondaryTickers = null) {
        var stock = CreateStock(ticker, name, secondaryTickers);
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        return stock;
    }

    private FailToDeliver CreateFtd(CommonStock stock, DateOnly? settlementDate = null, long quantity = 5000, decimal price = 150m) {
        return new FailToDeliver {
            Id = Guid.NewGuid(),
            CommonStock = stock,
            CommonStockId = stock.Id,
            SettlementDate = settlementDate ?? new DateOnly(2025, 3, 1),
            Quantity = quantity,
            Price = price,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // DocumentRepository
    // ═══════════════════════════════════════════════════════════════════

    // ── GetByCompany ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCompany_ReturnsOnlyDocumentsForGivenCompany() {
        var apple = await SeedStock("AAPL", "Apple");
        var msft = await SeedStock("MSFT", "Microsoft");

        _documentRepo.Add(CreateDocument(apple));
        _documentRepo.Add(CreateDocument(apple));
        _documentRepo.Add(CreateDocument(msft));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetByCompany(apple).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(d => d.CommonStockId.Should().Be(apple.Id));
    }

    [Fact]
    public async Task GetByCompany_NoDocuments_ReturnsEmpty() {
        var stock = await SeedStock();

        var result = await _documentRepo.GetByCompany(stock).ToListAsync();

        result.Should().BeEmpty();
    }

    // ── GetByTicker ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByTicker_MatchesPrimaryTicker_CaseInsensitive() {
        var stock = await SeedStock("AAPL", "Apple");
        _documentRepo.Add(CreateDocument(stock));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetByTicker("aapl").ToListAsync();

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetByTicker_MatchesSecondaryTicker() {
        var stock = await SeedStock("META", "Meta Platforms", ["FB"]);
        _documentRepo.Add(CreateDocument(stock));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetByTicker("FB").ToListAsync();

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetByTicker_NoMatch_ReturnsEmpty() {
        var stock = await SeedStock("AAPL", "Apple");
        _documentRepo.Add(CreateDocument(stock));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetByTicker("GOOG").ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByTicker_DoesNotReturnOtherCompanies() {
        var apple = await SeedStock("AAPL", "Apple");
        var msft = await SeedStock("MSFT", "Microsoft");
        _documentRepo.Add(CreateDocument(apple));
        _documentRepo.Add(CreateDocument(msft));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetByTicker("MSFT").ToListAsync();

        result.Should().ContainSingle()
            .Which.CommonStockId.Should().Be(msft.Id);
    }

    // ── GetByDocumentType ───────────────────────────────────────────────

    [Fact]
    public async Task GetByDocumentType_ReturnsOnlyMatchingType() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, DocumentType.TenK));
        _documentRepo.Add(CreateDocument(stock, DocumentType.TenQ));
        _documentRepo.Add(CreateDocument(stock, DocumentType.TenK));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetByDocumentType(DocumentType.TenK).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(d => d.DocumentType.Should().Be(DocumentType.TenK));
    }

    [Fact]
    public async Task GetByDocumentType_NoMatch_ReturnsEmpty() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, DocumentType.TenK));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetByDocumentType(DocumentType.EightK).ToListAsync();

        result.Should().BeEmpty();
    }

    // ── GetByDateRange ──────────────────────────────────────────────────

    [Fact]
    public async Task GetByDateRange_BothBounds_FiltersCorrectly() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 1, 1)));
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 6, 15)));
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 12, 31)));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo
            .GetByDateRange(new DateOnly(2025, 3, 1), new DateOnly(2025, 9, 1))
            .ToListAsync();

        result.Should().ContainSingle()
            .Which.ReportingDate.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public async Task GetByDateRange_OnlyFromDate_FiltersFromInclusive() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 1, 1)));
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 6, 15)));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo
            .GetByDateRange(fromDate: new DateOnly(2025, 6, 15))
            .ToListAsync();

        result.Should().ContainSingle()
            .Which.ReportingDate.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public async Task GetByDateRange_OnlyToDate_FiltersToInclusive() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 1, 1)));
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 6, 15)));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo
            .GetByDateRange(toDate: new DateOnly(2025, 1, 1))
            .ToListAsync();

        result.Should().ContainSingle()
            .Which.ReportingDate.Should().Be(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public async Task GetByDateRange_NoBounds_ReturnsAll() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2024, 1, 1)));
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 12, 31)));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetByDateRange().ToListAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByDateRange_NoMatches_ReturnsEmpty() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, reportingDate: new DateOnly(2025, 1, 1)));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo
            .GetByDateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
            .ToListAsync();

        result.Should().BeEmpty();
    }

    // ── Exists ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Exists_MatchingDocument_ReturnsTrue() {
        var stock = await SeedStock();
        var doc = CreateDocument(stock, DocumentType.TenK,
            reportingDate: new DateOnly(2025, 3, 15),
            reportingForDate: new DateOnly(2024, 12, 31));
        _documentRepo.Add(doc);
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.Exists(
            stock, DocumentType.TenK,
            new DateOnly(2025, 3, 15),
            new DateOnly(2024, 12, 31));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Exists_DifferentType_ReturnsFalse() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, DocumentType.TenK,
            reportingDate: new DateOnly(2025, 3, 15),
            reportingForDate: new DateOnly(2024, 12, 31)));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.Exists(
            stock, DocumentType.TenQ,
            new DateOnly(2025, 3, 15),
            new DateOnly(2024, 12, 31));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Exists_DifferentDate_ReturnsFalse() {
        var stock = await SeedStock();
        _documentRepo.Add(CreateDocument(stock, DocumentType.TenK,
            reportingDate: new DateOnly(2025, 3, 15),
            reportingForDate: new DateOnly(2024, 12, 31)));
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.Exists(
            stock, DocumentType.TenK,
            new DateOnly(2025, 4, 15),
            new DateOnly(2024, 12, 31));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Exists_EmptyDatabase_ReturnsFalse() {
        var stock = await SeedStock();

        var result = await _documentRepo.Exists(
            stock, DocumentType.TenK,
            new DateOnly(2025, 1, 1),
            new DateOnly(2024, 12, 31));

        result.Should().BeFalse();
    }

    // ── GetWithContent ──────────────────────────────────────────────────

    [Fact]
    public async Task GetWithContent_ExistingDocument_ReturnsDocument() {
        var stock = await SeedStock();
        var doc = CreateDocument(stock);
        _documentRepo.Add(doc);
        await _documentRepo.SaveChanges();

        var result = await _documentRepo.GetWithContent(doc.Id);

        result.Should().NotBeNull();
        result.Id.Should().Be(doc.Id);
    }

    [Fact]
    public async Task GetWithContent_NonExistentId_ReturnsNull() {
        var result = await _documentRepo.GetWithContent(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════
    // FailToDeliverRepository
    // ═══════════════════════════════════════════════════════════════════

    // ── GetByStock ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByStock_ReturnsOnlyFtdsForGivenStock() {
        var apple = await SeedStock("AAPL", "Apple");
        var msft = await SeedStock("MSFT", "Microsoft");

        _ftdRepo.Add(CreateFtd(apple, new DateOnly(2025, 1, 1)));
        _ftdRepo.Add(CreateFtd(apple, new DateOnly(2025, 1, 2)));
        _ftdRepo.Add(CreateFtd(msft, new DateOnly(2025, 1, 1)));
        await _ftdRepo.SaveChanges();

        var result = await _ftdRepo.GetByStock(apple).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(f => f.CommonStockId.Should().Be(apple.Id));
    }

    [Fact]
    public async Task GetByStock_NoFtds_ReturnsEmpty() {
        var stock = await SeedStock();

        var result = await _ftdRepo.GetByStock(stock).ToListAsync();

        result.Should().BeEmpty();
    }

    // ── GetLatestDate ───────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestDate_MultipleDates_ReturnsOnlyLatest() {
        var stock = await SeedStock();
        _ftdRepo.Add(CreateFtd(stock, new DateOnly(2025, 1, 1)));
        _ftdRepo.Add(CreateFtd(stock, new DateOnly(2025, 3, 15)));
        _ftdRepo.Add(CreateFtd(stock, new DateOnly(2025, 2, 10)));
        await _ftdRepo.SaveChanges();

        var result = await _ftdRepo.GetLatestDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2025, 3, 15));
    }

    [Fact]
    public async Task GetLatestDate_EmptyTable_ReturnsEmpty() {
        var result = await _ftdRepo.GetLatestDate().ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestDate_DuplicateDates_ReturnsDistinctLatest() {
        var apple = await SeedStock("AAPL", "Apple");
        var msft = await SeedStock("MSFT", "Microsoft");
        _ftdRepo.Add(CreateFtd(apple, new DateOnly(2025, 3, 15)));
        _ftdRepo.Add(CreateFtd(msft, new DateOnly(2025, 3, 15)));
        _ftdRepo.Add(CreateFtd(apple, new DateOnly(2025, 1, 1)));
        await _ftdRepo.SaveChanges();

        var result = await _ftdRepo.GetLatestDate().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2025, 3, 15));
    }

    // ── Base CRUD via FailToDeliverRepository ───────────────────────────

    [Fact]
    public async Task Ftd_Add_PersistsEntity() {
        var stock = await SeedStock();
        var ftd = CreateFtd(stock, new DateOnly(2025, 5, 1), 10000, 175.50m);

        _ftdRepo.Add(ftd);
        await _ftdRepo.SaveChanges();

        var result = await _ftdRepo.Get(ftd.Id);
        result.Should().NotBeNull();
        result.Quantity.Should().Be(10000);
        result.Price.Should().Be(175.50m);
        result.SettlementDate.Should().Be(new DateOnly(2025, 5, 1));
    }

    [Fact]
    public async Task Ftd_Delete_RemovesEntity() {
        var stock = await SeedStock();
        var ftd = CreateFtd(stock);
        _ftdRepo.Add(ftd);
        await _ftdRepo.SaveChanges();

        _ftdRepo.Delete(ftd);
        await _ftdRepo.SaveChanges();

        var result = await _ftdRepo.Get(ftd.Id);
        result.Should().BeNull();
    }
}
