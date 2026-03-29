using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Tests.Helpers;
using Equibles.Web.Services;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Equibles.Cftc.Data;
using Equibles.Cftc.Repositories;
using Equibles.Cboe.Data;
using Equibles.Cboe.Repositories;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Tests.Web;

public class DataCountServiceTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly DataCountService _service;

    public DataCountServiceTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new FredModuleConfiguration(),
            new YahooModuleConfiguration(),
            new CftcModuleConfiguration(),
            new CboeModuleConfiguration()
        );

        _service = new DataCountService(
            new CommonStockRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new InstitutionalHoldingRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new FredObservationRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new CftcPositionReportRepository(_dbContext),
            new CboePutCallRatioRepository(_dbContext),
            new CboeVixDailyRepository(_dbContext)
        );
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.", string cik = null) {
        var stock = new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = cik ?? Guid.NewGuid().ToString()[..10],
        };
        _dbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private File CreateFile() {
        return new File {
            Id = Guid.NewGuid(),
            Name = "filing",
            Extension = "html",
            ContentType = "text/html",
            Size = 1024,
            FileContent = new FileContent { Bytes = [0x01] },
        };
    }

    private InsiderOwner CreateInsiderOwner(string name = "John Doe", string ownerCik = null) {
        var owner = new InsiderOwner {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerCik = ownerCik ?? Guid.NewGuid().ToString()[..10],
        };
        _dbContext.Set<InsiderOwner>().Add(owner);
        return owner;
    }

    private CongressMember CreateCongressMember(string name = "Jane Smith") {
        var member = new CongressMember {
            Id = Guid.NewGuid(),
            Name = name,
            Position = CongressPosition.Senator,
        };
        _dbContext.Set<CongressMember>().Add(member);
        return member;
    }

    private InstitutionalHolder CreateInstitutionalHolder(string name = "Vanguard", string cik = null) {
        var holder = new InstitutionalHolder {
            Id = Guid.NewGuid(),
            Name = name,
            Cik = cik ?? Guid.NewGuid().ToString()[..10],
        };
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        return holder;
    }

    private FredSeries CreateFredSeries(string seriesId = "GDP", string title = "Gross Domestic Product") {
        var series = new FredSeries {
            Id = Guid.NewGuid(),
            SeriesId = seriesId,
            Title = title,
        };
        _dbContext.Set<FredSeries>().Add(series);
        return series;
    }

    // ── GetStockCount ───────────────────────────────────────────────────

    [Fact]
    public async Task GetStockCount_EmptyDatabase_ReturnsZero() {
        var result = await _service.GetStockCount();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetStockCount_WithStocks_ReturnsCorrectCount() {
        CreateStock("AAPL", "Apple Inc.", "0000320193");
        CreateStock("MSFT", "Microsoft Corp.", "0000789019");
        CreateStock("GOOG", "Alphabet Inc.", "0001652044");
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetStockCount();

        result.Should().Be(3);
    }

    // ── GetDocumentCount ────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentCount_EmptyDatabase_ReturnsZero() {
        var result = await _service.GetDocumentCount();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetDocumentCount_WithDocuments_ReturnsCorrectCount() {
        var stock = CreateStock();

        _dbContext.Set<Document>().AddRange(
            new Document {
                CommonStock = stock, CommonStockId = stock.Id,
                DocumentType = DocumentType.TenK,
                ReportingDate = new DateOnly(2025, 1, 15),
                ReportingForDate = new DateOnly(2024, 12, 31),
                Content = CreateFile(), SourceUrl = "https://sec.gov/1",
            },
            new Document {
                CommonStock = stock, CommonStockId = stock.Id,
                DocumentType = DocumentType.TenQ,
                ReportingDate = new DateOnly(2025, 4, 15),
                ReportingForDate = new DateOnly(2025, 3, 31),
                Content = CreateFile(), SourceUrl = "https://sec.gov/2",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDocumentCount();

        result.Should().Be(2);
    }

    // ── GetInsiderTransactionCount ──────────────────────────────────────

    [Fact]
    public async Task GetInsiderTransactionCount_EmptyDatabase_ReturnsZero() {
        var result = await _service.GetInsiderTransactionCount();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetInsiderTransactionCount_WithTransactions_ReturnsCorrectCount() {
        var stock = CreateStock();
        var owner = CreateInsiderOwner();

        _dbContext.Set<InsiderTransaction>().AddRange(
            new InsiderTransaction {
                CommonStockId = stock.Id, InsiderOwnerId = owner.Id,
                FilingDate = new DateOnly(2025, 3, 1), TransactionDate = new DateOnly(2025, 2, 28),
                TransactionCode = TransactionCode.Purchase, Shares = 1000, PricePerShare = 150m,
                SecurityTitle = "Common Stock", AccessionNumber = "0001-25-000001",
            },
            new InsiderTransaction {
                CommonStockId = stock.Id, InsiderOwnerId = owner.Id,
                FilingDate = new DateOnly(2025, 3, 15), TransactionDate = new DateOnly(2025, 3, 14),
                TransactionCode = TransactionCode.Sale, Shares = 500, PricePerShare = 155m,
                SecurityTitle = "Common Stock", AccessionNumber = "0001-25-000002",
            },
            new InsiderTransaction {
                CommonStockId = stock.Id, InsiderOwnerId = owner.Id,
                FilingDate = new DateOnly(2025, 4, 1), TransactionDate = new DateOnly(2025, 3, 31),
                TransactionCode = TransactionCode.Purchase, Shares = 2000, PricePerShare = 148m,
                SecurityTitle = "Common Stock", AccessionNumber = "0001-25-000003",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetInsiderTransactionCount();

        result.Should().Be(3);
    }

    // ── GetCongressionalTradeCount ──────────────────────────────────────

    [Fact]
    public async Task GetCongressionalTradeCount_EmptyDatabase_ReturnsZero() {
        var result = await _service.GetCongressionalTradeCount();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetCongressionalTradeCount_WithTrades_ReturnsCorrectCount() {
        var stock = CreateStock();
        var member = CreateCongressMember();

        _dbContext.Set<CongressionalTrade>().AddRange(
            new CongressionalTrade {
                CommonStockId = stock.Id, CongressMemberId = member.Id,
                TransactionDate = new DateOnly(2025, 1, 10), FilingDate = new DateOnly(2025, 2, 1),
                TransactionType = CongressTransactionType.Purchase,
                AssetName = "Apple Inc. Common Stock", AmountFrom = 1001, AmountTo = 15000,
            },
            new CongressionalTrade {
                CommonStockId = stock.Id, CongressMemberId = member.Id,
                TransactionDate = new DateOnly(2025, 3, 20), FilingDate = new DateOnly(2025, 4, 5),
                TransactionType = CongressTransactionType.Sale,
                AssetName = "Apple Inc. Options", AmountFrom = 15001, AmountTo = 50000,
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetCongressionalTradeCount();

        result.Should().Be(2);
    }

    // ── GetInstitutionalHoldingCount ────────────────────────────────────

    [Fact]
    public async Task GetInstitutionalHoldingCount_EmptyDatabase_ReturnsZero() {
        var result = await _service.GetInstitutionalHoldingCount();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetInstitutionalHoldingCount_WithHoldings_ReturnsCorrectCount() {
        var stock = CreateStock();
        var holder = CreateInstitutionalHolder();

        _dbContext.Set<InstitutionalHolding>().AddRange(
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
                FilingDate = new DateOnly(2025, 2, 14), ReportDate = new DateOnly(2024, 12, 31),
                Value = 500_000, Shares = 10_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000010",
            },
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
                FilingDate = new DateOnly(2025, 5, 15), ReportDate = new DateOnly(2025, 3, 31),
                Value = 600_000, Shares = 11_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000011",
            },
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
                FilingDate = new DateOnly(2025, 8, 14), ReportDate = new DateOnly(2025, 6, 30),
                Value = 700_000, Shares = 12_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000012",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetInstitutionalHoldingCount();

        result.Should().Be(3);
    }

    // ── GetFailToDeliverCount ───────────────────────────────────────────

    [Fact]
    public async Task GetFailToDeliverCount_EmptyDatabase_ReturnsZero() {
        var result = await _service.GetFailToDeliverCount();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetFailToDeliverCount_WithRecords_ReturnsCorrectCount() {
        var stock = CreateStock();

        _dbContext.Set<FailToDeliver>().AddRange(
            new FailToDeliver {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 1, 2),
                Quantity = 50_000, Price = 150.25m,
            },
            new FailToDeliver {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 1, 3),
                Quantity = 30_000, Price = 151.50m,
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetFailToDeliverCount();

        result.Should().Be(2);
    }

    // ── GetFredObservationCount ─────────────────────────────────────────

    [Fact]
    public async Task GetFredObservationCount_EmptyDatabase_ReturnsZero() {
        var result = await _service.GetFredObservationCount();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetFredObservationCount_WithObservations_ReturnsCorrectCount() {
        var series = CreateFredSeries();

        _dbContext.Set<FredObservation>().AddRange(
            new FredObservation {
                FredSeriesId = series.Id, Date = new DateOnly(2025, 1, 1), Value = 28_000.5m,
            },
            new FredObservation {
                FredSeriesId = series.Id, Date = new DateOnly(2025, 4, 1), Value = 28_500.0m,
            },
            new FredObservation {
                FredSeriesId = series.Id, Date = new DateOnly(2025, 7, 1), Value = 29_000.0m,
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetFredObservationCount();

        result.Should().Be(3);
    }

    // ── GetDailyStockPriceCount ─────────────────────────────────────────

    [Fact]
    public async Task GetDailyStockPriceCount_EmptyDatabase_ReturnsZero() {
        var result = await _service.GetDailyStockPriceCount();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetDailyStockPriceCount_WithPrices_ReturnsCorrectCount() {
        var stock = CreateStock();

        _dbContext.Set<DailyStockPrice>().AddRange(
            new DailyStockPrice {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 3, 24),
                Open = 170.00m, High = 172.50m, Low = 169.50m,
                Close = 171.25m, AdjustedClose = 171.25m, Volume = 45_000_000,
            },
            new DailyStockPrice {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 3, 25),
                Open = 171.25m, High = 173.00m, Low = 170.00m,
                Close = 172.50m, AdjustedClose = 172.50m, Volume = 50_000_000,
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDailyStockPriceCount();

        result.Should().Be(2);
    }
}
