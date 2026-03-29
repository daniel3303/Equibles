using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Holdings;

public class InstitutionalHolderRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly InstitutionalHolderRepository _repository;

    public InstitutionalHolderRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration(), new HoldingsModuleConfiguration());
        _repository = new InstitutionalHolderRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static InstitutionalHolder CreateHolder(
        string cik = "0001234567",
        string name = "Berkshire Hathaway Inc",
        string city = "Omaha",
        string stateOrCountry = "NE") {
        return new InstitutionalHolder {
            Id = Guid.NewGuid(),
            Cik = cik,
            Name = name,
            City = city,
            StateOrCountry = stateOrCountry,
        };
    }

    // ── GetByCik ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCik_ExistingCik_ReturnsHolder() {
        var holder = CreateHolder(cik: "0001067983");
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByCik("0001067983");

        result.Should().NotBeNull();
        result.Id.Should().Be(holder.Id);
        result.Cik.Should().Be("0001067983");
    }

    [Fact]
    public async Task GetByCik_NonExistentCik_ReturnsNull() {
        var holder = CreateHolder(cik: "0001067983");
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByCik("9999999999");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCik_EmptyDatabase_ReturnsNull() {
        var result = await _repository.GetByCik("0001067983");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCik_MultipleHolders_ReturnsCorrectOne() {
        _dbContext.Set<InstitutionalHolder>().AddRange(
            CreateHolder(cik: "0001067983", name: "Berkshire Hathaway"),
            CreateHolder(cik: "0001166559", name: "BlackRock Inc"),
            CreateHolder(cik: "0001364742", name: "Vanguard Group")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByCik("0001166559");

        result.Should().NotBeNull();
        result.Name.Should().Be("BlackRock Inc");
    }

    // ── GetByCiks ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCiks_MatchingCiks_ReturnsMatchingHolders() {
        _dbContext.Set<InstitutionalHolder>().AddRange(
            CreateHolder(cik: "0001067983", name: "Berkshire Hathaway"),
            CreateHolder(cik: "0001166559", name: "BlackRock Inc"),
            CreateHolder(cik: "0001364742", name: "Vanguard Group")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByCiks(["0001067983", "0001364742"]).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(h => h.Name).Should().BeEquivalentTo("Berkshire Hathaway", "Vanguard Group");
    }

    [Fact]
    public async Task GetByCiks_NoMatches_ReturnsEmpty() {
        _dbContext.Set<InstitutionalHolder>().Add(CreateHolder(cik: "0001067983"));
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByCiks(["9999999999", "8888888888"]).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCiks_EmptyInput_ReturnsEmpty() {
        _dbContext.Set<InstitutionalHolder>().Add(CreateHolder());
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByCiks([]).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCiks_PartialMatch_ReturnsOnlyMatching() {
        _dbContext.Set<InstitutionalHolder>().AddRange(
            CreateHolder(cik: "0001067983", name: "Berkshire Hathaway"),
            CreateHolder(cik: "0001166559", name: "BlackRock Inc")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByCiks(["0001067983", "0000000000"]).ToListAsync();

        result.Should().ContainSingle()
            .Which.Name.Should().Be("Berkshire Hathaway");
    }

    // ── Search ──────────────────────────────────────────────────────────
    // Note: Search uses EF.Functions.ILike which is PostgreSQL-specific.
    // The InMemory provider does not support ILike, so these tests verify
    // the method throws InvalidOperationException with InMemory, confirming
    // the method is wired to use the provider-specific function.
    // Full integration testing of Search requires a real PostgreSQL instance.

    [Fact]
    public async Task Search_InMemoryProvider_ThrowsBecauseILikeIsPostgresOnly() {
        _dbContext.Set<InstitutionalHolder>().Add(CreateHolder(name: "Berkshire Hathaway"));
        await _dbContext.SaveChangesAsync();

        var act = async () => await _repository.Search("Berkshire").ToListAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

public class InstitutionalHoldingRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration(), new HoldingsModuleConfiguration());
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc") {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = Guid.NewGuid().ToString()[..10],
        };
    }

    private static InstitutionalHolder CreateHolder(
        string cik = "0001067983",
        string name = "Berkshire Hathaway Inc") {
        return new InstitutionalHolder {
            Id = Guid.NewGuid(),
            Cik = cik,
            Name = name,
        };
    }

    private static InstitutionalHolding CreateHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        DateOnly? filingDate = null,
        long shares = 1000,
        long value = 50000,
        string accessionNumber = "0000000000-24-000001") {
        return new InstitutionalHolding {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            CommonStock = stock,
            InstitutionalHolderId = holder.Id,
            InstitutionalHolder = holder,
            ReportDate = reportDate,
            FilingDate = filingDate ?? reportDate.AddDays(45),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accessionNumber,
            TitleOfClass = "COM",
            Cusip = "037833100",
        };
    }

    private async Task<(CommonStock stock, InstitutionalHolder holder)> SeedStockAndHolder(
        string ticker = "AAPL",
        string holderCik = "0001067983",
        string holderName = "Berkshire Hathaway Inc") {
        var stock = CreateStock(ticker);
        var holder = CreateHolder(holderCik, holderName);
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();
        return (stock, holder);
    }

    // ── GetByStock ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByStock_MatchingReportDate_ReturnsHoldings() {
        var (stock, holder) = await SeedStockAndHolder();
        var reportDate = new DateOnly(2024, 3, 31);
        var holding = CreateHolding(stock, holder, reportDate);
        _dbContext.Set<InstitutionalHolding>().Add(holding);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(stock, reportDate).ToListAsync();

        result.Should().ContainSingle()
            .Which.Shares.Should().Be(1000);
    }

    [Fact]
    public async Task GetByStock_DifferentReportDate_ReturnsEmpty() {
        var (stock, holder) = await SeedStockAndHolder();
        var holding = CreateHolding(stock, holder, new DateOnly(2024, 3, 31));
        _dbContext.Set<InstitutionalHolding>().Add(holding);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(stock, new DateOnly(2024, 6, 30)).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_MultipleHolders_ReturnsAllForDate() {
        var stock = CreateStock("AAPL");
        var berkshire = CreateHolder("0001067983", "Berkshire Hathaway");
        var blackrock = CreateHolder("0001166559", "BlackRock Inc");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(berkshire, blackrock);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, berkshire, reportDate, shares: 5000, accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, blackrock, reportDate, shares: 8000, accessionNumber: "0000000000-24-000002")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(stock, reportDate).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(h => h.Shares).Should().BeEquivalentTo([5000L, 8000L]);
    }

    [Fact]
    public async Task GetByStock_FiltersOutDifferentStocks() {
        var apple = CreateStock("AAPL");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var holder = CreateHolder();
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(apple, holder, reportDate, accessionNumber: "0000000000-24-000001"),
            CreateHolding(msft, holder, reportDate, accessionNumber: "0000000000-24-000002")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByStock(apple, reportDate).ToListAsync();

        result.Should().ContainSingle()
            .Which.CommonStockId.Should().Be(apple.Id);
    }

    // ── GetHistoryByStock ───────────────────────────────────────────────

    [Fact]
    public async Task GetHistoryByStock_MultipleReportDates_ReturnsAll() {
        var (stock, holder) = await SeedStockAndHolder();
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, new DateOnly(2024, 3, 31), shares: 1000, accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder, new DateOnly(2024, 6, 30), shares: 1500, accessionNumber: "0000000000-24-000002"),
            CreateHolding(stock, holder, new DateOnly(2024, 9, 30), shares: 2000, accessionNumber: "0000000000-24-000003")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByStock(stock).ToListAsync();

        result.Should().HaveCount(3);
        result.Select(h => h.Shares).Should().BeEquivalentTo([1000L, 1500L, 2000L]);
    }

    [Fact]
    public async Task GetHistoryByStock_NoHoldings_ReturnsEmpty() {
        var (stock, _) = await SeedStockAndHolder();

        var result = await _repository.GetHistoryByStock(stock).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryByStock_ExcludesOtherStocks() {
        var apple = CreateStock("AAPL");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var holder = CreateHolder();
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(apple, holder, new DateOnly(2024, 3, 31), accessionNumber: "0000000000-24-000001"),
            CreateHolding(msft, holder, new DateOnly(2024, 3, 31), accessionNumber: "0000000000-24-000002")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByStock(apple).ToListAsync();

        result.Should().ContainSingle()
            .Which.CommonStockId.Should().Be(apple.Id);
    }

    // ── GetByHolder ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByHolder_MatchingReportDate_ReturnsHoldings() {
        var (stock, holder) = await SeedStockAndHolder();
        var reportDate = new DateOnly(2024, 3, 31);
        var holding = CreateHolding(stock, holder, reportDate);
        _dbContext.Set<InstitutionalHolding>().Add(holding);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByHolder(holder, reportDate).ToListAsync();

        result.Should().ContainSingle()
            .Which.CommonStockId.Should().Be(stock.Id);
    }

    [Fact]
    public async Task GetByHolder_DifferentReportDate_ReturnsEmpty() {
        var (stock, holder) = await SeedStockAndHolder();
        var holding = CreateHolding(stock, holder, new DateOnly(2024, 3, 31));
        _dbContext.Set<InstitutionalHolding>().Add(holding);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByHolder(holder, new DateOnly(2024, 12, 31)).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByHolder_MultipleStocks_ReturnsAllForDate() {
        var apple = CreateStock("AAPL");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var holder = CreateHolder();
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(apple, holder, reportDate, accessionNumber: "0000000000-24-000001"),
            CreateHolding(msft, holder, reportDate, accessionNumber: "0000000000-24-000002")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByHolder(holder, reportDate).ToListAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByHolder_FiltersOutOtherHolders() {
        var stock = CreateStock("AAPL");
        var berkshire = CreateHolder("0001067983", "Berkshire Hathaway");
        var blackrock = CreateHolder("0001166559", "BlackRock Inc");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(berkshire, blackrock);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, berkshire, reportDate, accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, blackrock, reportDate, accessionNumber: "0000000000-24-000002")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByHolder(berkshire, reportDate).ToListAsync();

        result.Should().ContainSingle()
            .Which.InstitutionalHolderId.Should().Be(berkshire.Id);
    }

    // ── GetHistoryByHolder ──────────────────────────────────────────────

    [Fact]
    public async Task GetHistoryByHolder_MultipleReportDates_ReturnsAll() {
        var (stock, holder) = await SeedStockAndHolder();
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, new DateOnly(2024, 3, 31), accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder, new DateOnly(2024, 6, 30), accessionNumber: "0000000000-24-000002"),
            CreateHolding(stock, holder, new DateOnly(2024, 9, 30), accessionNumber: "0000000000-24-000003")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByHolder(holder).ToListAsync();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetHistoryByHolder_NoHoldings_ReturnsEmpty() {
        var (_, holder) = await SeedStockAndHolder();

        var result = await _repository.GetHistoryByHolder(holder).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryByHolder_ExcludesOtherHolders() {
        var stock = CreateStock("AAPL");
        var berkshire = CreateHolder("0001067983", "Berkshire Hathaway");
        var blackrock = CreateHolder("0001166559", "BlackRock Inc");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(berkshire, blackrock);
        await _dbContext.SaveChangesAsync();

        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, berkshire, new DateOnly(2024, 3, 31), accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, blackrock, new DateOnly(2024, 3, 31), accessionNumber: "0000000000-24-000002")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetHistoryByHolder(berkshire).ToListAsync();

        result.Should().ContainSingle()
            .Which.InstitutionalHolderId.Should().Be(berkshire.Id);
    }

    // ── GetAvailableReportDates ─────────────────────────────────────────

    [Fact]
    public async Task GetAvailableReportDates_MultipleDistinctDates_ReturnsDistinct() {
        var (stock, holder) = await SeedStockAndHolder();
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, new DateOnly(2024, 3, 31), accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder, new DateOnly(2024, 6, 30), accessionNumber: "0000000000-24-000002"),
            CreateHolding(stock, holder, new DateOnly(2024, 9, 30), accessionNumber: "0000000000-24-000003")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetAvailableReportDates().ToListAsync();

        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo([
            new DateOnly(2024, 3, 31),
            new DateOnly(2024, 6, 30),
            new DateOnly(2024, 9, 30),
        ]);
    }

    [Fact]
    public async Task GetAvailableReportDates_DuplicateDates_ReturnsDistinct() {
        var stock = CreateStock("AAPL");
        var berkshire = CreateHolder("0001067983", "Berkshire Hathaway");
        var blackrock = CreateHolder("0001166559", "BlackRock Inc");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(berkshire, blackrock);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, berkshire, reportDate, accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, blackrock, reportDate, accessionNumber: "0000000000-24-000002")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetAvailableReportDates().ToListAsync();

        result.Should().ContainSingle()
            .Which.Should().Be(reportDate);
    }

    [Fact]
    public async Task GetAvailableReportDates_NoHoldings_ReturnsEmpty() {
        var result = await _repository.GetAvailableReportDates().ToListAsync();

        result.Should().BeEmpty();
    }

    // ── GetByAccessionNumber ────────────────────────────────────────────

    [Fact]
    public async Task GetByAccessionNumber_MatchingNumber_ReturnsHoldings() {
        var (stock, holder) = await SeedStockAndHolder();
        var holding = CreateHolding(stock, holder, new DateOnly(2024, 3, 31), accessionNumber: "0001067983-24-000042");
        _dbContext.Set<InstitutionalHolding>().Add(holding);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByAccessionNumber("0001067983-24-000042").ToListAsync();

        result.Should().ContainSingle()
            .Which.AccessionNumber.Should().Be("0001067983-24-000042");
    }

    [Fact]
    public async Task GetByAccessionNumber_NoMatch_ReturnsEmpty() {
        var (stock, holder) = await SeedStockAndHolder();
        _dbContext.Set<InstitutionalHolding>().Add(
            CreateHolding(stock, holder, new DateOnly(2024, 3, 31), accessionNumber: "0001067983-24-000042")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByAccessionNumber("9999999999-24-999999").ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByAccessionNumber_MultipleHoldingsSameAccession_ReturnsAll() {
        var apple = CreateStock("AAPL");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var holder = CreateHolder();
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        var accession = "0001067983-24-000042";
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(apple, holder, new DateOnly(2024, 3, 31), accessionNumber: accession),
            CreateHolding(msft, holder, new DateOnly(2024, 3, 31), accessionNumber: accession)
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByAccessionNumber(accession).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(h => h.CommonStockId).Should().BeEquivalentTo([apple.Id, msft.Id]);
    }

    [Fact]
    public async Task GetByAccessionNumber_DifferentAccessions_ReturnsOnlyMatching() {
        var (stock, holder) = await SeedStockAndHolder();
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, new DateOnly(2024, 3, 31), accessionNumber: "0001067983-24-000042"),
            CreateHolding(stock, holder, new DateOnly(2024, 6, 30), accessionNumber: "0001067983-24-000099")
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByAccessionNumber("0001067983-24-000042").ToListAsync();

        result.Should().ContainSingle()
            .Which.ReportDate.Should().Be(new DateOnly(2024, 3, 31));
    }
}
