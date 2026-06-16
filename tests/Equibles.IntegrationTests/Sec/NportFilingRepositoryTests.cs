using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

public class NportFilingRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly NportFilingRepository _repository;

    public NportFilingRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _repository = new NportFilingRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker = "VOO", string cik = "0000036405")
    {
        return new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = cik,
        };
    }

    private static NportFiling CreateFiling(
        Guid commonStockId,
        string accessionNumber = "0000036405-24-000002",
        DateOnly? filingDate = null,
        string seriesName = "Vanguard 500 Index Fund",
        string seriesId = "S000002277"
    )
    {
        return new NportFiling
        {
            Id = Guid.NewGuid(),
            CommonStockId = commonStockId,
            AccessionNumber = accessionNumber,
            FilingDate = filingDate ?? new DateOnly(2025, 1, 15),
            IsAmendment = false,
            RegistrantName = "VANGUARD INDEX FUNDS",
            SeriesName = seriesName,
            SeriesId = seriesId,
            SeriesLei = "5493007GHODQRGNTGS28",
            ReportPeriodDate = new DateOnly(2024, 12, 31),
            ReportPeriodEnd = new DateOnly(2025, 12, 31),
            TotalAssets = 1_200_000_000m,
            TotalLiabilities = 50_000_000m,
            NetAssets = 1_150_000_000m,
            IsFinalFiling = false,
        };
    }

    // A sweep-discovered filing: no tracked stock, registrant identified by CIK.
    private static NportFiling CreateTrustFiling(
        string registrantCik,
        string accessionNumber,
        DateOnly? filingDate = null,
        string seriesName = "Vanguard 500 Index Fund",
        string seriesId = "S000002277"
    )
    {
        var filing = CreateFiling(
            commonStockId: Guid.Empty,
            accessionNumber,
            filingDate,
            seriesName,
            seriesId
        );
        filing.CommonStockId = null;
        filing.RegistrantCik = registrantCik;
        return filing;
    }

    [Fact]
    public async Task GetByStock_ReturnsOnlyFilingsForThatStock()
    {
        var voo = CreateStock("VOO", "0000036405");
        var other = CreateStock("SPY", "0000884394");
        _dbContext.Set<CommonStock>().AddRange(voo, other);
        await _dbContext.SaveChangesAsync();

        _repository.Add(CreateFiling(voo.Id, "0000036405-24-000002"));
        _repository.Add(CreateFiling(voo.Id, "0000036405-23-000003"));
        _repository.Add(CreateFiling(other.Id, "0000884394-24-000001"));
        await _repository.SaveChanges();

        var result = await _repository.GetByStock(voo).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(f => f.CommonStockId == voo.Id);
    }

    [Fact]
    public async Task GetByAccessionNumber_ExistingAccession_ReturnsFiling()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0000036405-24-000002"));
        await _repository.SaveChanges();

        var result = await _repository
            .GetByAccessionNumber("0000036405-24-000002")
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result.SeriesName.Should().Be("Vanguard 500 Index Fund");
    }

    [Fact]
    public async Task GetByAccessionNumber_NonExistentAccession_ReturnsEmpty()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateFiling(stock.Id, "0000036405-24-000002"));
        await _repository.SaveChanges();

        var any = await _repository.GetByAccessionNumber("9999999999-99-999999").AnyAsync();

        any.Should().BeFalse();
    }

    [Fact]
    public async Task Add_FilingWithHoldings_PersistsChildRows()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var filing = CreateFiling(stock.Id);
        filing.Holdings.Add(
            new NportHolding
            {
                Name = "AT&T Inc",
                Title = "AT&T Inc",
                Cusip = "00206R102",
                Isin = "US00206R1023",
                Balance = 112500m,
                Units = "NS",
                Currency = "USD",
                ValueUsd = 1_794_375m,
                PercentValue = 0.49m,
                PayoffProfile = "Long",
                AssetCategory = "EC",
                IssuerCategory = "CORP",
                InvestmentCountry = "US",
            }
        );
        filing.Holdings.Add(
            new NportHolding
            {
                Name = "US Treasury Note",
                Balance = 5_000_000m,
                Units = "PA",
                Currency = "USD",
                ValueUsd = 4_900_000m,
                PercentValue = 4.26m,
                PayoffProfile = "Long",
                AssetCategory = "DBT",
                IssuerCategory = "UST",
            }
        );
        _repository.Add(filing);
        await _repository.SaveChanges();

        var loaded = await _repository
            .GetByAccessionNumber(filing.AccessionNumber)
            .Include(f => f.Holdings)
            .FirstAsync();

        loaded.Holdings.Should().HaveCount(2);
        loaded
            .Holdings.Should()
            .Contain(h =>
                h.Name == "AT&T Inc" && h.Cusip == "00206R102" && h.AssetCategory == "EC"
            );
    }

    [Fact]
    public async Task GetLatestPerSeries_ReturnsTheNewestReportPerSeries()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var older = CreateFiling(stock.Id, "0000036405-24-000001", new DateOnly(2024, 11, 20));
        older.ReportPeriodDate = new DateOnly(2024, 10, 31);
        var newest = CreateFiling(stock.Id, "0000036405-25-000002", new DateOnly(2025, 1, 15));
        newest.ReportPeriodDate = new DateOnly(2024, 12, 31);
        var otherSeries = CreateFiling(
            stock.Id,
            "0000036405-25-000003",
            new DateOnly(2025, 1, 15),
            "Vanguard Growth Index Fund",
            "S000002278"
        );
        otherSeries.ReportPeriodDate = new DateOnly(2024, 12, 31);
        var belowFloor = CreateFiling(
            stock.Id,
            "0000036405-23-000004",
            new DateOnly(2023, 1, 15),
            "Vanguard Stale Fund",
            "S000002279"
        );
        belowFloor.ReportPeriodDate = new DateOnly(2022, 12, 31);
        _repository.Add(older);
        _repository.Add(newest);
        _repository.Add(otherSeries);
        _repository.Add(belowFloor);
        await _repository.SaveChanges();

        var result = await _repository.GetLatestPerSeries(new DateOnly(2024, 1, 1)).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(f => f.Id).Should().BeEquivalentTo([newest.Id, otherSeries.Id]);
    }

    // A listed closed-end fund files with no series id, and its name text drifts across filings
    // ("Inc" vs "Inc.", stray spaces, a legal rename). All such filings are the registrant's
    // single fund, so only the newest report may surface — name variants must never each freeze
    // their own stale "latest".
    [Fact]
    public async Task GetLatestPerSeries_IdlessNameVariants_CollapseToTheNewestReport()
    {
        var stock = CreateStock("CLM", "0000814083");
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var renamed = CreateFiling(
            stock.Id,
            "0001752724-21-000001",
            new DateOnly(2021, 8, 20),
            "Cornerstone Strategic Value Fund, Inc.",
            seriesId: null
        );
        renamed.ReportPeriodDate = new DateOnly(2021, 6, 30);
        var straySpace = CreateFiling(
            stock.Id,
            "0001752724-25-000002",
            new DateOnly(2025, 8, 22),
            "Cornerstone Strategic Investment Fund , Inc",
            seriesId: null
        );
        straySpace.ReportPeriodDate = new DateOnly(2025, 6, 30);
        var newest = CreateFiling(
            stock.Id,
            "0000910472-26-000003",
            new DateOnly(2026, 5, 21),
            "Cornerstone Strategic Investment Fund, Inc.",
            seriesId: null
        );
        newest.ReportPeriodDate = new DateOnly(2026, 3, 31);
        _repository.Add(renamed);
        _repository.Add(straySpace);
        _repository.Add(newest);
        await _repository.SaveChanges();

        var result = await _repository.GetLatestPerSeries(DateOnly.MinValue).ToListAsync();

        result.Should().ContainSingle().Which.Id.Should().Be(newest.Id);
    }

    // Some funds report their series id only on part of their filings. An id-less report is the
    // registrant's single fund, so a newer id-carrying report of the same stock supersedes it —
    // otherwise the fund's pre-id era would survive as a second, stale "series".
    [Fact]
    public async Task GetLatestPerSeries_IdlessOlderReport_SupersededByNewerIdCarryingReport()
    {
        var stock = CreateStock("FMN", "0001212422");
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var idless = CreateFiling(
            stock.Id,
            "0001623632-21-000001",
            new DateOnly(2021, 7, 28),
            "Federated Premier Municipal Income Fund",
            seriesId: null
        );
        idless.ReportPeriodDate = new DateOnly(2021, 5, 31);
        var idCarrying = CreateFiling(
            stock.Id,
            "0001623632-26-000002",
            new DateOnly(2026, 1, 28),
            "Federated Hermes Premier Municipal Income Fund",
            "S000011351"
        );
        idCarrying.ReportPeriodDate = new DateOnly(2025, 11, 30);
        _repository.Add(idless);
        _repository.Add(idCarrying);
        await _repository.SaveChanges();

        var result = await _repository.GetLatestPerSeries(DateOnly.MinValue).ToListAsync();

        result.Should().ContainSingle().Which.Id.Should().Be(idCarrying.Id);
    }

    // Two filings carrying different non-empty series ids are genuinely different series of the
    // same registrant — a shared or similar name must not collapse them.
    [Fact]
    public async Task GetLatestPerSeries_DistinctSeriesIdsWithSameName_BothSurvive()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var seriesA = CreateFiling(
            stock.Id,
            "0000036405-26-000001",
            new DateOnly(2026, 1, 15),
            "Vanguard Index Fund",
            "S000002277"
        );
        seriesA.ReportPeriodDate = new DateOnly(2025, 12, 31);
        var seriesB = CreateFiling(
            stock.Id,
            "0000036405-26-000002",
            new DateOnly(2026, 2, 15),
            "Vanguard Index Fund",
            "S000002278"
        );
        seriesB.ReportPeriodDate = new DateOnly(2026, 1, 31);
        _repository.Add(seriesA);
        _repository.Add(seriesB);
        await _repository.SaveChanges();

        var result = await _repository.GetLatestPerSeries(DateOnly.MinValue).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(f => f.Id).Should().BeEquivalentTo([seriesA.Id, seriesB.Id]);
    }

    [Fact]
    public async Task GetHoldingsByCusip_ReturnsOnlyRowsCarryingThatCusip()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        var filing = CreateFiling(stock.Id);
        filing.Holdings.Add(
            new NportHolding
            {
                Name = "APPLE INC",
                Cusip = "037833100",
                Balance = 100m,
                Units = "NS",
                Currency = "USD",
                ValueUsd = 25_000m,
                PercentValue = 2.17m,
                PayoffProfile = "Long",
                AssetCategory = "EC",
                IssuerCategory = "CORP",
            }
        );
        filing.Holdings.Add(
            new NportHolding
            {
                Name = "MICROSOFT CORP",
                Cusip = "594918104",
                Balance = 50m,
                Units = "NS",
                Currency = "USD",
                ValueUsd = 21_000m,
                PercentValue = 1.83m,
                PayoffProfile = "Long",
                AssetCategory = "EC",
                IssuerCategory = "CORP",
            }
        );
        _repository.Add(filing);
        await _repository.SaveChanges();

        var result = await _repository.GetHoldingsByCusip("037833100").ToListAsync();

        result.Should().ContainSingle().Which.Name.Should().Be("APPLE INC");
    }

    // Sweep-discovered filings carry no CommonStockId; their series is scoped by registrant CIK.
    // Only the newest report of a trust series may surface, exactly as for tracked stocks.
    [Fact]
    public async Task GetLatestPerSeries_TrustOnlyFilings_ScopedByRegistrantCikNewestWins()
    {
        var older = CreateTrustFiling("36405", "0000036405-24-000001", new DateOnly(2024, 11, 20));
        older.ReportPeriodDate = new DateOnly(2024, 10, 31);
        var newest = CreateTrustFiling("36405", "0000036405-25-000002", new DateOnly(2025, 1, 15));
        newest.ReportPeriodDate = new DateOnly(2024, 12, 31);
        _repository.Add(older);
        _repository.Add(newest);
        await _repository.SaveChanges();

        var result = await _repository.GetLatestPerSeries(DateOnly.MinValue).ToListAsync();

        result.Should().ContainSingle().Which.Id.Should().Be(newest.Id);
    }

    // Two id-less trust filings from different registrants are different funds — registrant CIK,
    // not a shared null CommonStockId, must keep them apart (otherwise every trust-only id-less
    // filing would collapse into one).
    [Fact]
    public async Task GetLatestPerSeries_IdlessTrustFilingsDifferentRegistrants_BothSurvive()
    {
        var fundA = CreateTrustFiling(
            "111111",
            "0001111111-26-000001",
            new DateOnly(2026, 1, 15),
            "Cohen Closed-End Fund",
            seriesId: null
        );
        fundA.ReportPeriodDate = new DateOnly(2025, 12, 31);
        var fundB = CreateTrustFiling(
            "222222",
            "0002222222-26-000002",
            new DateOnly(2026, 2, 15),
            "Clough Closed-End Fund",
            seriesId: null
        );
        fundB.ReportPeriodDate = new DateOnly(2026, 1, 31);
        _repository.Add(fundA);
        _repository.Add(fundB);
        await _repository.SaveChanges();

        var result = await _repository.GetLatestPerSeries(DateOnly.MinValue).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(f => f.Id).Should().BeEquivalentTo([fundA.Id, fundB.Id]);
    }

    [Fact]
    public async Task GetByRegistrantCikAndSeries_KnownSeries_ReturnsIt()
    {
        _repository.Add(CreateTrustFiling("36405", "0000036405-25-000002"));
        await _repository.SaveChanges();

        var known = await _repository.GetByRegistrantCikAndSeries("36405", "S000002277").AnyAsync();
        var unknownSeries = await _repository
            .GetByRegistrantCikAndSeries("36405", "S999999999")
            .AnyAsync();
        var unknownRegistrant = await _repository
            .GetByRegistrantCikAndSeries("99999", "S000002277")
            .AnyAsync();

        known.Should().BeTrue();
        unknownSeries.Should().BeFalse();
        unknownRegistrant.Should().BeFalse();
    }
}
