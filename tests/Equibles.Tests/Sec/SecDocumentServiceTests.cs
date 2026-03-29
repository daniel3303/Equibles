using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Sec;

public class SecDocumentServiceTests {
    private readonly SecDocumentService _sut;
    private readonly DocumentRepository _documentRepository;
    private readonly Equibles.Data.EquiblesDbContext _context;

    public SecDocumentServiceTests() {
        _context = TestDbContextFactory.Create(
            new SecTestModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration());
        _documentRepository = new DocumentRepository(_context);
        var logger = Substitute.For<ILogger<SecDocumentService>>();
        _sut = new SecDocumentService(_documentRepository, logger);
    }

    private CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") {
        var stock = new CommonStock {
            Ticker = ticker,
            Name = name,
            Cik = Guid.NewGuid().ToString(),
        };
        _context.Set<CommonStock>().Add(stock);
        _context.SaveChanges();
        return stock;
    }

    private Document CreateDocument(CommonStock stock, DocumentType docType, DateOnly reportingDate, DateOnly reportingForDate) {
        var file = new Equibles.Media.Data.Models.File {
            Name = "test",
            Extension = "html",
            ContentType = "text/html",
            Size = 100,
        };
        _context.Set<Equibles.Media.Data.Models.File>().Add(file);

        var doc = new Document {
            CommonStock = stock,
            CommonStockId = stock.Id,
            DocumentType = docType,
            ReportingDate = reportingDate,
            ReportingForDate = reportingForDate,
            LineCount = 100,
            Content = file,
            ContentId = file.Id,
        };
        _context.Set<Document>().Add(doc);
        _context.SaveChanges();
        return doc;
    }

    [Fact]
    public async Task GetRecentDocuments_NullTicker_ThrowsApplicationException() {
        var act = () => _sut.GetRecentDocuments(null);

        await act.Should().ThrowAsync<ApplicationException>().WithMessage("*Ticker*null*");
    }

    [Fact]
    public async Task GetRecentDocuments_NoDocuments_ReturnsEmpty() {
        CreateStock("AAPL");

        var result = await _sut.GetRecentDocuments("AAPL");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentDocuments_ReturnsMatchingDocuments() {
        var stock = CreateStock("AAPL");
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2024, 1, 1), new DateOnly(2023, 12, 31));

        var result = await _sut.GetRecentDocuments("AAPL");

        result.Should().ContainSingle();
        result[0].Ticker.Should().Be("AAPL");
        result[0].CompanyName.Should().Be("Apple Inc.");
    }

    [Fact]
    public async Task GetRecentDocuments_FilterByStartDate() {
        var stock = CreateStock("AAPL");
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2023, 6, 1), new DateOnly(2023, 3, 31));
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2024, 6, 1), new DateOnly(2024, 3, 31));

        var result = await _sut.GetRecentDocuments("AAPL", startDate: new DateTime(2024, 1, 1));

        result.Should().ContainSingle();
        result[0].ReportingDate.Should().Be(new DateOnly(2024, 6, 1));
    }

    [Fact]
    public async Task GetRecentDocuments_FilterByEndDate() {
        var stock = CreateStock("AAPL");
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2023, 6, 1), new DateOnly(2023, 3, 31));
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2024, 6, 1), new DateOnly(2024, 3, 31));

        var result = await _sut.GetRecentDocuments("AAPL", endDate: new DateTime(2023, 12, 31));

        result.Should().ContainSingle();
        result[0].ReportingDate.Should().Be(new DateOnly(2023, 6, 1));
    }

    [Fact]
    public async Task GetRecentDocuments_FilterByDocumentType() {
        var stock = CreateStock("AAPL");
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2024, 1, 1), new DateOnly(2023, 12, 31));
        CreateDocument(stock, DocumentType.TenQ, new DateOnly(2024, 4, 1), new DateOnly(2024, 3, 31));

        var result = await _sut.GetRecentDocuments("AAPL", documentType: DocumentType.TenQ);

        result.Should().ContainSingle();
        result[0].DocumentType.Should().Be(DocumentType.TenQ);
    }

    [Fact]
    public async Task GetRecentDocuments_Pagination_RespectsMaxItemsAndPage() {
        var stock = CreateStock("AAPL");
        for (var i = 1; i <= 5; i++) {
            CreateDocument(stock, DocumentType.TenQ,
                new DateOnly(2024, i, 1), new DateOnly(2024, i, 1));
        }

        var page1 = await _sut.GetRecentDocuments("AAPL", maxItems: 2, page: 1);
        var page2 = await _sut.GetRecentDocuments("AAPL", maxItems: 2, page: 2);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1.Select(d => d.Id).Should().NotIntersectWith(page2.Select(d => d.Id));
    }

    [Fact]
    public async Task GetRecentDocuments_OrderByReportingDateDescending() {
        var stock = CreateStock("AAPL");
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2022, 1, 1), new DateOnly(2021, 12, 31));
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2024, 1, 1), new DateOnly(2023, 12, 31));
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2023, 1, 1), new DateOnly(2022, 12, 31));

        var result = await _sut.GetRecentDocuments("AAPL");

        result.Should().HaveCount(3);
        result[0].ReportingDate.Should().Be(new DateOnly(2024, 1, 1));
        result[1].ReportingDate.Should().Be(new DateOnly(2023, 1, 1));
        result[2].ReportingDate.Should().Be(new DateOnly(2022, 1, 1));
    }

    [Fact]
    public async Task GetRecentDocuments_DifferentTicker_ReturnsEmpty() {
        var stock = CreateStock("AAPL");
        CreateDocument(stock, DocumentType.TenK, new DateOnly(2024, 1, 1), new DateOnly(2023, 12, 31));

        var result = await _sut.GetRecentDocuments("MSFT");

        result.Should().BeEmpty();
    }
}
