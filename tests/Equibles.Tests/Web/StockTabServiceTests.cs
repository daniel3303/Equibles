using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
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
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Tests.Web;

public class StockTabServiceTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly StockTabService _service;

    public StockTabServiceTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new FinraModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new YahooModuleConfiguration()
        );

        _service = new StockTabService(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext)
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

    // ── LoadShortVolumeTab ──────────────────────────────────────────────

    [Fact]
    public async Task LoadShortVolumeTab_WithVolumes_ReturnsDataOrderedByDateAscending() {
        var stock = CreateStock();
        _dbContext.Set<DailyShortVolume>().AddRange(
            new DailyShortVolume {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 3, 10),
                ShortVolume = 500_000, ShortExemptVolume = 1_000, TotalVolume = 1_200_000, Market = "NYSE",
            },
            new DailyShortVolume {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 3, 11),
                ShortVolume = 600_000, ShortExemptVolume = 1_500, TotalVolume = 1_300_000, Market = "NYSE",
            },
            new DailyShortVolume {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 3, 12),
                ShortVolume = 550_000, ShortExemptVolume = 1_200, TotalVolume = 1_250_000, Market = "NYSE",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadShortVolumeTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.ShortVolumes.Should().HaveCount(3);
        result.ShortVolumes.First().Date.Should().Be(new DateOnly(2025, 3, 10));
        result.ShortVolumes.Last().Date.Should().Be(new DateOnly(2025, 3, 12));
    }

    [Fact]
    public async Task LoadShortVolumeTab_NoVolumes_ReturnsEmptyList() {
        var stock = CreateStock();
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadShortVolumeTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.ShortVolumes.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadShortVolumeTab_MoreThan90Records_ReturnsOnly90MostRecent() {
        var stock = CreateStock();
        for (var i = 0; i < 100; i++) {
            _dbContext.Set<DailyShortVolume>().Add(new DailyShortVolume {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 1, 1).AddDays(i),
                ShortVolume = 100_000 + i, ShortExemptVolume = 100, TotalVolume = 500_000, Market = "NYSE",
            });
        }
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadShortVolumeTab(stock);

        result.ShortVolumes.Should().HaveCount(90);
        // Should contain only the 90 most recent, ordered ascending
        result.ShortVolumes.First().Date.Should().Be(new DateOnly(2025, 1, 1).AddDays(10));
        result.ShortVolumes.Last().Date.Should().Be(new DateOnly(2025, 1, 1).AddDays(99));
    }

    [Fact]
    public async Task LoadShortVolumeTab_DoesNotReturnOtherStocksData() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.", "0000789019");
        _dbContext.Set<DailyShortVolume>().AddRange(
            new DailyShortVolume {
                CommonStockId = apple.Id, Date = new DateOnly(2025, 3, 10),
                ShortVolume = 500_000, TotalVolume = 1_200_000, Market = "NYSE",
            },
            new DailyShortVolume {
                CommonStockId = msft.Id, Date = new DateOnly(2025, 3, 10),
                ShortVolume = 300_000, TotalVolume = 900_000, Market = "NASDAQ",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadShortVolumeTab(apple);

        result.ShortVolumes.Should().HaveCount(1);
        result.ShortVolumes.Single().ShortVolume.Should().Be(500_000);
    }

    // ── LoadShortInterestTab ────────────────────────────────────────────

    [Fact]
    public async Task LoadShortInterestTab_WithData_ReturnsOrderedBySettlementDateAscending() {
        var stock = CreateStock();
        _dbContext.Set<ShortInterest>().AddRange(
            new ShortInterest {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 1, 15),
                CurrentShortPosition = 10_000_000, PreviousShortPosition = 9_500_000,
                ChangeInShortPosition = 500_000, AverageDailyVolume = 50_000_000, DaysToCover = 0.2m,
            },
            new ShortInterest {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 1, 31),
                CurrentShortPosition = 10_500_000, PreviousShortPosition = 10_000_000,
                ChangeInShortPosition = 500_000, AverageDailyVolume = 48_000_000, DaysToCover = 0.22m,
            },
            new ShortInterest {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 2, 14),
                CurrentShortPosition = 11_000_000, PreviousShortPosition = 10_500_000,
                ChangeInShortPosition = 500_000, AverageDailyVolume = 52_000_000, DaysToCover = 0.21m,
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadShortInterestTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.ShortInterests.Should().HaveCount(3);
        result.ShortInterests.First().SettlementDate.Should().Be(new DateOnly(2025, 1, 15));
        result.ShortInterests.Last().SettlementDate.Should().Be(new DateOnly(2025, 2, 14));
    }

    [Fact]
    public async Task LoadShortInterestTab_NoData_ReturnsEmptyList() {
        var stock = CreateStock();
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadShortInterestTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.ShortInterests.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadShortInterestTab_MoreThan24Records_ReturnsOnly24MostRecent() {
        var stock = CreateStock();
        for (var i = 0; i < 30; i++) {
            _dbContext.Set<ShortInterest>().Add(new ShortInterest {
                CommonStockId = stock.Id,
                SettlementDate = new DateOnly(2024, 1, 15).AddDays(i * 15),
                CurrentShortPosition = 10_000_000 + i * 100_000,
                PreviousShortPosition = 10_000_000 + (i - 1) * 100_000,
                ChangeInShortPosition = 100_000,
            });
        }
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadShortInterestTab(stock);

        result.ShortInterests.Should().HaveCount(24);
    }

    // ── LoadFtdTab ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadFtdTab_WithData_ReturnsOrderedBySettlementDateAscending() {
        var stock = CreateStock();
        _dbContext.Set<FailToDeliver>().AddRange(
            new FailToDeliver {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 1, 2),
                Quantity = 50_000, Price = 150.25m,
            },
            new FailToDeliver {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 1, 3),
                Quantity = 30_000, Price = 151.50m,
            },
            new FailToDeliver {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 1, 6),
                Quantity = 45_000, Price = 149.75m,
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadFtdTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.FailsToDeliver.Should().HaveCount(3);
        result.FailsToDeliver.First().SettlementDate.Should().Be(new DateOnly(2025, 1, 2));
        result.FailsToDeliver.Last().SettlementDate.Should().Be(new DateOnly(2025, 1, 6));
    }

    [Fact]
    public async Task LoadFtdTab_NoData_ReturnsEmptyList() {
        var stock = CreateStock();
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadFtdTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.FailsToDeliver.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadFtdTab_MoreThan90Records_ReturnsOnly90MostRecent() {
        var stock = CreateStock();
        for (var i = 0; i < 100; i++) {
            _dbContext.Set<FailToDeliver>().Add(new FailToDeliver {
                CommonStockId = stock.Id, SettlementDate = new DateOnly(2025, 1, 1).AddDays(i),
                Quantity = 10_000 + i, Price = 150m,
            });
        }
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadFtdTab(stock);

        result.FailsToDeliver.Should().HaveCount(90);
    }

    // ── LoadDocumentsTab ────────────────────────────────────────────────

    [Fact]
    public async Task LoadDocumentsTab_WithDocuments_ReturnsDocumentsOrderedByReportingDateDescending() {
        var stock = CreateStock();
        _dbContext.Set<Document>().AddRange(
            new Document {
                CommonStockId = stock.Id, CommonStock = stock,
                DocumentType = DocumentType.TenK,
                ReportingDate = new DateOnly(2025, 2, 15),
                ReportingForDate = new DateOnly(2024, 12, 31),
                Content = CreateFile(), SourceUrl = "https://sec.gov/1",
            },
            new Document {
                CommonStockId = stock.Id, CommonStock = stock,
                DocumentType = DocumentType.TenQ,
                ReportingDate = new DateOnly(2025, 5, 1),
                ReportingForDate = new DateOnly(2025, 3, 31),
                Content = CreateFile(), SourceUrl = "https://sec.gov/2",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadDocumentsTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.Documents.Should().HaveCount(2);
        result.Documents.First().ReportingDate.Should().Be(new DateOnly(2025, 5, 1));
        result.Documents.Last().ReportingDate.Should().Be(new DateOnly(2025, 2, 15));
    }

    [Fact]
    public async Task LoadDocumentsTab_NoDocuments_ReturnsEmptyList() {
        var stock = CreateStock();
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadDocumentsTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadDocumentsTab_DoesNotReturnOtherStocksDocuments() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.", "0000789019");
        _dbContext.Set<Document>().AddRange(
            new Document {
                CommonStockId = apple.Id, CommonStock = apple,
                DocumentType = DocumentType.TenK,
                ReportingDate = new DateOnly(2025, 2, 15),
                ReportingForDate = new DateOnly(2024, 12, 31),
                Content = CreateFile(), SourceUrl = "https://sec.gov/1",
            },
            new Document {
                CommonStockId = msft.Id, CommonStock = msft,
                DocumentType = DocumentType.TenQ,
                ReportingDate = new DateOnly(2025, 5, 1),
                ReportingForDate = new DateOnly(2025, 3, 31),
                Content = CreateFile(), SourceUrl = "https://sec.gov/2",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadDocumentsTab(apple);

        result.Documents.Should().HaveCount(1);
        result.Documents.Single().DocumentType.Should().Be(DocumentType.TenK);
    }

    // ── LoadInsiderTradingTab ───────────────────────────────────────────

    [Fact]
    public async Task LoadInsiderTradingTab_WithTransactions_ReturnsOrderedByTransactionDateDescending() {
        var stock = CreateStock();
        var owner = CreateInsiderOwner();
        _dbContext.Set<InsiderTransaction>().AddRange(
            new InsiderTransaction {
                CommonStockId = stock.Id, InsiderOwnerId = owner.Id,
                FilingDate = new DateOnly(2025, 3, 1), TransactionDate = new DateOnly(2025, 2, 28),
                TransactionCode = TransactionCode.Purchase, Shares = 1_000, PricePerShare = 150m,
                SecurityTitle = "Common Stock", AccessionNumber = "0001-25-000001",
            },
            new InsiderTransaction {
                CommonStockId = stock.Id, InsiderOwnerId = owner.Id,
                FilingDate = new DateOnly(2025, 3, 15), TransactionDate = new DateOnly(2025, 3, 14),
                TransactionCode = TransactionCode.Sale, Shares = 500, PricePerShare = 155m,
                SecurityTitle = "Common Stock", AccessionNumber = "0001-25-000002",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadInsiderTradingTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.Transactions.Should().HaveCount(2);
        result.Transactions.First().TransactionDate.Should().Be(new DateOnly(2025, 3, 14));
        result.Transactions.Last().TransactionDate.Should().Be(new DateOnly(2025, 2, 28));
    }

    [Fact]
    public async Task LoadInsiderTradingTab_NoTransactions_ReturnsEmptyList() {
        var stock = CreateStock();
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadInsiderTradingTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadInsiderTradingTab_IncludesInsiderOwnerNavigation() {
        var stock = CreateStock();
        var owner = CreateInsiderOwner("Tim Cook", "0001234567");
        _dbContext.Set<InsiderTransaction>().Add(new InsiderTransaction {
            CommonStockId = stock.Id, InsiderOwnerId = owner.Id,
            FilingDate = new DateOnly(2025, 3, 1), TransactionDate = new DateOnly(2025, 2, 28),
            TransactionCode = TransactionCode.Sale, Shares = 50_000, PricePerShare = 175m,
            SecurityTitle = "Common Stock", AccessionNumber = "0001-25-000010",
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadInsiderTradingTab(stock);

        result.Transactions.Single().InsiderOwner.Should().NotBeNull();
        result.Transactions.Single().InsiderOwner.Name.Should().Be("Tim Cook");
    }

    // ── LoadCongressionalTradesTab ──────────────────────────────────────

    [Fact]
    public async Task LoadCongressionalTradesTab_WithTrades_ReturnsOrderedByTransactionDateDescending() {
        var stock = CreateStock();
        var member = CreateCongressMember("Nancy Pelosi");
        _dbContext.Set<CongressionalTrade>().AddRange(
            new CongressionalTrade {
                CommonStockId = stock.Id, CongressMemberId = member.Id,
                TransactionDate = new DateOnly(2025, 1, 10), FilingDate = new DateOnly(2025, 2, 1),
                TransactionType = CongressTransactionType.Purchase,
                AssetName = "Apple Inc. Common Stock", AmountFrom = 1_001, AmountTo = 15_000,
            },
            new CongressionalTrade {
                CommonStockId = stock.Id, CongressMemberId = member.Id,
                TransactionDate = new DateOnly(2025, 3, 20), FilingDate = new DateOnly(2025, 4, 5),
                TransactionType = CongressTransactionType.Sale,
                AssetName = "Apple Inc. Options", AmountFrom = 15_001, AmountTo = 50_000,
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadCongressionalTradesTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.Trades.Should().HaveCount(2);
        result.Trades.First().TransactionDate.Should().Be(new DateOnly(2025, 3, 20));
        result.Trades.Last().TransactionDate.Should().Be(new DateOnly(2025, 1, 10));
    }

    [Fact]
    public async Task LoadCongressionalTradesTab_NoTrades_ReturnsEmptyList() {
        var stock = CreateStock();
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadCongressionalTradesTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.Trades.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadCongressionalTradesTab_IncludesCongressMemberNavigation() {
        var stock = CreateStock();
        var member = CreateCongressMember("Dan Crenshaw");
        _dbContext.Set<CongressionalTrade>().Add(new CongressionalTrade {
            CommonStockId = stock.Id, CongressMemberId = member.Id,
            TransactionDate = new DateOnly(2025, 2, 15), FilingDate = new DateOnly(2025, 3, 1),
            TransactionType = CongressTransactionType.Purchase,
            AssetName = "Apple Inc.", AmountFrom = 1_001, AmountTo = 15_000,
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadCongressionalTradesTab(stock);

        result.Trades.Single().CongressMember.Should().NotBeNull();
        result.Trades.Single().CongressMember.Name.Should().Be("Dan Crenshaw");
    }

    // ── LoadPriceTab ────────────────────────────────────────────────────

    [Fact]
    public async Task LoadPriceTab_WithPrices_ReturnsPricesAndTechnicalIndicators() {
        var stock = CreateStock();
        // Insert enough prices to produce at least one SMA-20 value
        for (var i = 0; i < 30; i++) {
            _dbContext.Set<DailyStockPrice>().Add(new DailyStockPrice {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 1, 1).AddDays(i),
                Open = 100m + i, High = 102m + i, Low = 99m + i,
                Close = 101m + i, AdjustedClose = 101m + i, Volume = 10_000_000,
            });
        }
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadPriceTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.Prices.Should().HaveCount(30);
        result.Prices.First().Date.Should().Be(new DateOnly(2025, 1, 1));
        result.Prices.Last().Date.Should().Be(new DateOnly(2025, 1, 30));

        // SMA-20 should have 19 nulls then 11 values
        result.Sma20.Should().HaveCount(30);
        result.Sma20.Take(19).Should().AllSatisfy(v => v.Should().BeNull());
        result.Sma20.Skip(19).Should().AllSatisfy(v => v.Should().NotBeNull());

        // SMA-50 should be all null with only 30 data points
        result.Sma50.Should().HaveCount(30);
        result.Sma50.Should().AllSatisfy(v => v.Should().BeNull());

        // RSI-14 should have some non-null values (requires 15+ data points)
        result.Rsi14.Should().HaveCount(30);
        result.Rsi14.Skip(14).Should().AllSatisfy(v => v.Should().NotBeNull());

        // MACD lists should match price count
        result.MacdLine.Should().HaveCount(30);
        result.MacdSignal.Should().HaveCount(30);
        result.MacdHistogram.Should().HaveCount(30);
    }

    [Fact]
    public async Task LoadPriceTab_NoPrices_ReturnsEmptyListsAndEmptyIndicators() {
        var stock = CreateStock();
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadPriceTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.Prices.Should().BeEmpty();
        result.Sma20.Should().BeEmpty();
        result.Sma50.Should().BeEmpty();
        result.Sma200.Should().BeEmpty();
        result.Rsi14.Should().BeEmpty();
        result.MacdLine.Should().BeEmpty();
        result.MacdSignal.Should().BeEmpty();
        result.MacdHistogram.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPriceTab_PricesReturnedInAscendingDateOrder() {
        var stock = CreateStock();
        // Insert out of order
        _dbContext.Set<DailyStockPrice>().AddRange(
            new DailyStockPrice {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 3, 3),
                Open = 103m, High = 105m, Low = 102m, Close = 104m, AdjustedClose = 104m, Volume = 10_000_000,
            },
            new DailyStockPrice {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 3, 1),
                Open = 100m, High = 102m, Low = 99m, Close = 101m, AdjustedClose = 101m, Volume = 12_000_000,
            },
            new DailyStockPrice {
                CommonStockId = stock.Id, Date = new DateOnly(2025, 3, 2),
                Open = 101m, High = 103m, Low = 100m, Close = 102m, AdjustedClose = 102m, Volume = 11_000_000,
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadPriceTab(stock);

        result.Prices.Select(p => p.Date).Should().BeInAscendingOrder();
    }

    // ── LoadHoldingsTab ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadHoldingsTab_NullDate_SelectsLatestReportDate() {
        var stock = CreateStock();
        var holder = CreateInstitutionalHolder("Vanguard", "0001234567");
        // Single report date avoids GroupBy path that InMemory provider cannot translate
        _dbContext.Set<InstitutionalHolding>().Add(new InstitutionalHolding {
            CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
            FilingDate = new DateOnly(2025, 5, 15), ReportDate = new DateOnly(2025, 3, 31),
            Value = 600_000, Shares = 11_000, ShareType = ShareType.Shares,
            TitleOfClass = "COM", AccessionNumber = "0001-25-000011",
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadHoldingsTab(stock, null);

        result.SelectedDate.Should().Be(new DateOnly(2025, 3, 31));
        result.Holdings.Should().HaveCount(1);
        result.Holdings.Single().Shares.Should().Be(11_000);
        result.TotalShares.Should().Be(11_000);
        result.TotalValue.Should().Be(600_000);
        result.HolderCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadHoldingsTab_ExplicitDate_SelectsThatDate() {
        var stock = CreateStock();
        var holder = CreateInstitutionalHolder("BlackRock", "0009876543");
        _dbContext.Set<InstitutionalHolding>().AddRange(
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
                FilingDate = new DateOnly(2025, 2, 14), ReportDate = new DateOnly(2024, 12, 31),
                Value = 500_000, Shares = 10_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000020",
            },
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
                FilingDate = new DateOnly(2025, 5, 15), ReportDate = new DateOnly(2025, 3, 31),
                Value = 600_000, Shares = 11_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000021",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadHoldingsTab(stock, new DateOnly(2024, 12, 31));

        result.SelectedDate.Should().Be(new DateOnly(2024, 12, 31));
        result.Holdings.Should().HaveCount(1);
        result.Holdings.Single().Shares.Should().Be(10_000);
    }

    [Fact]
    public async Task LoadHoldingsTab_NoHoldings_ReturnsEmptyViewModel() {
        var stock = CreateStock();
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadHoldingsTab(stock, null);

        result.Ticker.Should().Be("AAPL");
        result.Holdings.Should().BeEmpty();
        result.AvailableDates.Should().BeEmpty();
        result.TotalShares.Should().Be(0);
        result.TotalValue.Should().Be(0);
        result.HolderCount.Should().Be(0);
    }

    /// <summary>
    /// The previous quarter lookup in LoadHoldingsTab uses GroupBy + ToDictionaryAsync which the
    /// EF Core InMemory provider cannot translate when owned entities (HoldingManagerEntry) are
    /// auto-included. This test verifies the behavior against a real PostgreSQL database and is
    /// skipped in unit tests. The logic is implicitly validated: when only one report date exists,
    /// PreviousSharesByHolder is empty (see NoPreviousQuarter test); the GroupBy path is
    /// exercised in integration tests.
    /// </summary>
    [Fact(Skip = "InMemory provider cannot translate GroupBy with owned entity collections")]
    public async Task LoadHoldingsTab_PreviousQuarterLookup_PopulatesPreviousSharesByHolder() {
        var stock = CreateStock();
        var holder = CreateInstitutionalHolder("State Street", "0001112223");

        var q4Date = new DateOnly(2024, 12, 31);
        var q1Date = new DateOnly(2025, 3, 31);

        _dbContext.Set<InstitutionalHolding>().AddRange(
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
                FilingDate = new DateOnly(2025, 2, 14), ReportDate = q4Date,
                Value = 400_000, Shares = 8_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000030",
            },
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
                FilingDate = new DateOnly(2025, 5, 15), ReportDate = q1Date,
                Value = 600_000, Shares = 12_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000031",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadHoldingsTab(stock, q1Date);

        result.PreviousSharesByHolder.Should().ContainKey(holder.Id);
        result.PreviousSharesByHolder[holder.Id].Should().Be(8_000);
    }

    [Fact]
    public async Task LoadHoldingsTab_NoPreviousQuarter_PreviousSharesByHolderIsEmpty() {
        var stock = CreateStock();
        var holder = CreateInstitutionalHolder("Fidelity", "0004445556");

        // Only one quarter of data
        _dbContext.Set<InstitutionalHolding>().Add(new InstitutionalHolding {
            CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
            FilingDate = new DateOnly(2025, 2, 14), ReportDate = new DateOnly(2024, 12, 31),
            Value = 500_000, Shares = 10_000, ShareType = ShareType.Shares,
            TitleOfClass = "COM", AccessionNumber = "0001-25-000040",
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadHoldingsTab(stock, new DateOnly(2024, 12, 31));

        result.PreviousSharesByHolder.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadHoldingsTab_AvailableDatesOrderedDescending() {
        var stock = CreateStock();
        var holder = CreateInstitutionalHolder("T. Rowe Price", "0007778889");

        var dates = new[] {
            new DateOnly(2024, 6, 30), new DateOnly(2024, 9, 30),
            new DateOnly(2024, 12, 31), new DateOnly(2025, 3, 31),
        };

        foreach (var date in dates) {
            _dbContext.Set<InstitutionalHolding>().Add(new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder.Id,
                FilingDate = date.AddMonths(1), ReportDate = date,
                Value = 500_000, Shares = 10_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = $"0001-{date:yyyyMMdd}",
            });
        }
        await _dbContext.SaveChangesAsync();

        // Select the oldest date so the previous-quarter GroupBy path is not triggered
        // (selectedIndex == last index, so selectedIndex < Count - 1 is false)
        var oldestDate = new DateOnly(2024, 6, 30);
        var result = await _service.LoadHoldingsTab(stock, oldestDate);

        result.AvailableDates.Should().BeInDescendingOrder();
        result.AvailableDates.Should().HaveCount(4);
        result.AvailableDates.First().Should().Be(new DateOnly(2025, 3, 31));
    }

    [Fact]
    public async Task LoadHoldingsTab_MultipleHolders_AggregatesCorrectly() {
        var stock = CreateStock();
        var holder1 = CreateInstitutionalHolder("Vanguard", "0001000001");
        var holder2 = CreateInstitutionalHolder("BlackRock", "0001000002");
        var reportDate = new DateOnly(2025, 3, 31);

        _dbContext.Set<InstitutionalHolding>().AddRange(
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder1.Id,
                FilingDate = new DateOnly(2025, 5, 15), ReportDate = reportDate,
                Value = 500_000, Shares = 10_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000050",
            },
            new InstitutionalHolding {
                CommonStockId = stock.Id, InstitutionalHolderId = holder2.Id,
                FilingDate = new DateOnly(2025, 5, 15), ReportDate = reportDate,
                Value = 300_000, Shares = 6_000, ShareType = ShareType.Shares,
                TitleOfClass = "COM", AccessionNumber = "0001-25-000051",
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.LoadHoldingsTab(stock, reportDate);

        result.TotalShares.Should().Be(16_000);
        result.TotalValue.Should().Be(800_000);
        result.HolderCount.Should().Be(2);
        result.DisplayedCount.Should().Be(2);
        result.Holdings.Should().HaveCount(2);
        // Ordered by value descending
        result.Holdings.First().Value.Should().Be(500_000);
        result.Holdings.Last().Value.Should().Be(300_000);
    }
}
