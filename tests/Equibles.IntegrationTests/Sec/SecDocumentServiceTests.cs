using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class SecDocumentServiceTests
{
    private readonly SecDocumentService _sut;
    private readonly DocumentRepository _documentRepository;
    private readonly Equibles.Data.EquiblesFinancialDbContext _context;

    public SecDocumentServiceTests()
    {
        _context = TestDbContextFactory.Create(
            new SecTestModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration()
        );
        _documentRepository = new DocumentRepository(_context);
        var logger = Substitute.For<ILogger<SecDocumentService>>();
        _sut = new SecDocumentService(_documentRepository, logger);
    }

    private EquityIssuer CreateStock(string ticker = "AAPL", string name = "Apple Inc.")
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: name,
            Cik: Guid.NewGuid().ToString()
        );
        _context.Set<EquityIssuer>().Add(stock);
        _context.SaveChanges();
        return stock;
    }

    private Document CreateDocument(
        EquityIssuer stock,
        DocumentType docType,
        DateOnly reportingDate,
        DateOnly reportingForDate,
        string items = null,
        Guid? id = null
    )
    {
        var file = new Equibles.Media.Data.Models.File
        {
            Name = "test",
            Extension = "html",
            ContentType = "text/html",
            Size = 100,
        };
        _context.Set<Equibles.Media.Data.Models.File>().Add(file);

        var doc = new Document
        {
            Id = id ?? Guid.NewGuid(),
            EquityIssuerId = stock.Id,
            DocumentType = docType,
            ReportingDate = reportingDate,
            ReportingForDate = reportingForDate,
            Items = items,
            LineCount = 100,
            Content = file,
            ContentId = file.Id,
        };
        _context.Set<Document>().Add(doc);
        _context.SaveChanges();
        return doc;
    }

    [Fact]
    public async Task GetRecentDocuments_NullTicker_ReturnsMarketWideDocuments()
    {
        EquityIssuer apple = CreateStock("AAPL", "Apple Inc.");
        EquityIssuer microsoft = CreateStock("MSFT", "Microsoft Corp.");
        CreateDocument(
            apple,
            DocumentType.TenK,
            new DateOnly(2024, 1, 1),
            new DateOnly(2023, 12, 31)
        );
        CreateDocument(
            microsoft,
            DocumentType.TenQ,
            new DateOnly(2024, 4, 1),
            new DateOnly(2024, 3, 31)
        );

        var result = await _sut.GetRecentDocuments();

        result.Select(document => document.Ticker).Should().Equal("MSFT", "AAPL");
    }

    [Fact]
    public async Task GetRecentDocuments_SameFilingDate_OrdersByAcceptanceThenIngestion()
    {
        // Ids ascend opposite to the expected order, and the morning filing was ingested last,
        // so neither an id nor an ingestion-time order can pass for the acceptance order.
        var stock = CreateStock();
        var day = new DateOnly(2026, 10, 15);
        var noon = CreateDocument(stock, DocumentType.EightK, day, day, id: Ordered(1));
        noon.AcceptedAt = new DateTime(2026, 10, 15, 12, 5, 7, DateTimeKind.Utc);
        noon.CreationTime = new DateTime(2026, 10, 15, 12, 14, 47, DateTimeKind.Utc);
        var unaccepted = CreateDocument(stock, DocumentType.TenQ, day, day, id: Ordered(2));
        unaccepted.CreationTime = new DateTime(2026, 10, 15, 11, 30, 0, DateTimeKind.Utc);
        var morning = CreateDocument(stock, DocumentType.EightK, day, day, id: Ordered(3));
        morning.AcceptedAt = new DateTime(2026, 10, 15, 11, 0, 33, DateTimeKind.Utc);
        morning.CreationTime = new DateTime(2026, 10, 15, 12, 30, 0, DateTimeKind.Utc);
        var earlierDay = CreateDocument(
            stock,
            DocumentType.EightK,
            day.AddDays(-1),
            day,
            id: Ordered(4)
        );
        earlierDay.AcceptedAt = new DateTime(2026, 10, 14, 23, 0, 0, DateTimeKind.Utc);
        await _context.SaveChangesAsync();

        var result = await _sut.GetRecentDocuments("AAPL");

        result.Select(d => d.Id).Should().Equal(noon.Id, unaccepted.Id, morning.Id, earlierDay.Id);
    }

    [Fact]
    public async Task GetRecentDocuments_FilingWithoutAPeriod_ReturnsANullPeriod()
    {
        var stock = CreateStock();
        var day = new DateOnly(2026, 10, 15);
        var registration = CreateDocument(stock, DocumentType.SThree, day, DateOnly.MinValue);
        var quarter = CreateDocument(stock, DocumentType.TenQ, day, new DateOnly(2026, 9, 30));

        var result = await _sut.GetRecentDocuments("AAPL");

        result.Single(d => d.Id == registration.Id).ReportingForDate.Should().BeNull();
        result
            .Single(d => d.Id == quarter.Id)
            .ReportingForDate.Should()
            .Be(new DateOnly(2026, 9, 30));
    }

    private static Guid Ordered(int value) => new($"00000000-0000-0000-0000-{value:D12}");

    [Fact]
    public async Task GetRecentDocuments_NoDocuments_ReturnsEmpty()
    {
        CreateStock("AAPL");

        var result = await _sut.GetRecentDocuments("AAPL");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentDocuments_ReturnsMatchingDocuments()
    {
        EquityIssuer stock = CreateStock("AAPL");
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2024, 1, 1),
            new DateOnly(2023, 12, 31)
        );

        var result = await _sut.GetRecentDocuments("AAPL");

        result.Should().ContainSingle();
        result[0].Ticker.Should().Be("AAPL");
        result[0].CompanyName.Should().Be("Apple Inc.");
    }

    [Fact]
    public async Task GetRecentDocuments_FilterByStartDate()
    {
        EquityIssuer stock = CreateStock("AAPL");
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2023, 6, 1),
            new DateOnly(2023, 3, 31)
        );
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2024, 6, 1),
            new DateOnly(2024, 3, 31)
        );

        var result = await _sut.GetRecentDocuments("AAPL", startDate: new DateTime(2024, 1, 1));

        result.Should().ContainSingle();
        result[0].ReportingDate.Should().Be(new DateOnly(2024, 6, 1));
    }

    [Fact]
    public async Task GetRecentDocuments_FilterByEndDate()
    {
        EquityIssuer stock = CreateStock("AAPL");
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2023, 6, 1),
            new DateOnly(2023, 3, 31)
        );
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2024, 6, 1),
            new DateOnly(2024, 3, 31)
        );

        var result = await _sut.GetRecentDocuments("AAPL", endDate: new DateTime(2023, 12, 31));

        result.Should().ContainSingle();
        result[0].ReportingDate.Should().Be(new DateOnly(2023, 6, 1));
    }

    [Fact]
    public async Task GetRecentDocuments_FilterByDocumentType()
    {
        EquityIssuer stock = CreateStock("AAPL");
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2024, 1, 1),
            new DateOnly(2023, 12, 31)
        );
        CreateDocument(
            stock,
            DocumentType.TenQ,
            new DateOnly(2024, 4, 1),
            new DateOnly(2024, 3, 31)
        );

        var result = await _sut.GetRecentDocuments("AAPL", documentType: DocumentType.TenQ);

        result.Should().ContainSingle();
        result[0].DocumentType.Should().Be(DocumentType.TenQ);
    }

    [Fact]
    public async Task GetRecentDocuments_FilterByItemNumber_UsesExactTokenMatch()
    {
        EquityIssuer stock = CreateStock("AAPL");
        CreateDocument(
            stock,
            DocumentType.EightK,
            new DateOnly(2024, 1, 1),
            new DateOnly(2023, 12, 31),
            "2.02,9.01"
        );
        CreateDocument(
            stock,
            DocumentType.EightK,
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 1, 31),
            "1.02"
        );

        var result = await _sut.GetRecentDocuments("AAPL", itemNumber: "2.02");

        result.Should().ContainSingle();
        result[0].Items.Should().Be("2.02,9.01");
    }

    [Fact]
    public async Task GetRecentDocuments_Pagination_RespectsMaxItemsAndPage()
    {
        EquityIssuer stock = CreateStock("AAPL");
        for (var i = 1; i <= 5; i++)
        {
            CreateDocument(
                stock,
                DocumentType.TenQ,
                new DateOnly(2024, i, 1),
                new DateOnly(2024, i, 1)
            );
        }

        var page1 = await _sut.GetRecentDocuments("AAPL", maxItems: 2, page: 1);
        var page2 = await _sut.GetRecentDocuments("AAPL", maxItems: 2, page: 2);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1.Select(d => d.Id).Should().NotIntersectWith(page2.Select(d => d.Id));
    }

    [Fact]
    public async Task GetRecentDocuments_OrderByReportingDateDescending()
    {
        EquityIssuer stock = CreateStock("AAPL");
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2022, 1, 1),
            new DateOnly(2021, 12, 31)
        );
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2024, 1, 1),
            new DateOnly(2023, 12, 31)
        );
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2023, 1, 1),
            new DateOnly(2022, 12, 31)
        );

        var result = await _sut.GetRecentDocuments("AAPL");

        result.Should().HaveCount(3);
        result[0].ReportingDate.Should().Be(new DateOnly(2024, 1, 1));
        result[1].ReportingDate.Should().Be(new DateOnly(2023, 1, 1));
        result[2].ReportingDate.Should().Be(new DateOnly(2022, 1, 1));
    }

    [Fact]
    public async Task GetRecentDocuments_DifferentTicker_ReturnsEmpty()
    {
        EquityIssuer stock = CreateStock("AAPL");
        CreateDocument(
            stock,
            DocumentType.TenK,
            new DateOnly(2024, 1, 1),
            new DateOnly(2023, 12, 31)
        );

        var result = await _sut.GetRecentDocuments("MSFT");

        result.Should().BeEmpty();
    }
}
