using Equibles.Data;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Fred;

public class FredSeriesRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly FredSeriesRepository _repository;

    public FredSeriesRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new FredModuleConfiguration());
        _repository = new FredSeriesRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static FredSeries CreateSeries(
        string seriesId = "DFF",
        string title = "Federal Funds Effective Rate",
        FredSeriesCategory category = FredSeriesCategory.InterestRates,
        string frequency = "Daily",
        string units = "Percent",
        string seasonalAdjustment = "Not Seasonally Adjusted") {
        return new FredSeries {
            Id = Guid.NewGuid(),
            SeriesId = seriesId,
            Title = title,
            Category = category,
            Frequency = frequency,
            Units = units,
            SeasonalAdjustment = seasonalAdjustment,
        };
    }

    // ── GetBySeriesId ──────────────────────────────────────────────────

    [Fact]
    public async Task GetBySeriesId_ExistingSeries_ReturnsSeries() {
        var series = CreateSeries("DFF", "Federal Funds Effective Rate");
        _dbContext.Set<FredSeries>().Add(series);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetBySeriesId("DFF").FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result!.SeriesId.Should().Be("DFF");
        result.Title.Should().Be("Federal Funds Effective Rate");
    }

    [Fact]
    public async Task GetBySeriesId_NonExistentId_ReturnsNull() {
        var result = await _repository.GetBySeriesId("NONEXISTENT").FirstOrDefaultAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBySeriesId_DoesNotReturnOtherSeries() {
        _dbContext.Set<FredSeries>().AddRange(
            CreateSeries("DFF", "Federal Funds Effective Rate"),
            CreateSeries("DGS10", "10-Year Treasury Constant Maturity Rate")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetBySeriesId("DFF").ToList();

        result.Should().ContainSingle()
            .Which.SeriesId.Should().Be("DFF");
    }

    // ── GetByCategory ──────────────────────────────────────────────────

    [Fact]
    public async Task GetByCategory_ReturnSeriesInCategory() {
        _dbContext.Set<FredSeries>().AddRange(
            CreateSeries("DFF", "Federal Funds", FredSeriesCategory.InterestRates),
            CreateSeries("DGS10", "10Y Treasury", FredSeriesCategory.InterestRates),
            CreateSeries("CPIAUCSL", "CPI for All Urban Consumers", FredSeriesCategory.Inflation)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByCategory(FredSeriesCategory.InterestRates).ToList();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(s => s.Category.Should().Be(FredSeriesCategory.InterestRates));
    }

    [Fact]
    public async Task GetByCategory_EmptyCategory_ReturnsEmpty() {
        _dbContext.Set<FredSeries>().Add(
            CreateSeries("DFF", "Federal Funds", FredSeriesCategory.InterestRates)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByCategory(FredSeriesCategory.Housing).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCategory_AllCategories_FiltersCorrectly() {
        _dbContext.Set<FredSeries>().AddRange(
            CreateSeries("DFF", "Federal Funds", FredSeriesCategory.InterestRates),
            CreateSeries("CPIAUCSL", "CPI", FredSeriesCategory.Inflation),
            CreateSeries("UNRATE", "Unemployment Rate", FredSeriesCategory.Employment),
            CreateSeries("GDP", "Gross Domestic Product", FredSeriesCategory.GdpAndOutput)
        );
        await _dbContext.SaveChangesAsync();

        _repository.GetByCategory(FredSeriesCategory.InterestRates).ToList().Should().ContainSingle();
        _repository.GetByCategory(FredSeriesCategory.Inflation).ToList().Should().ContainSingle();
        _repository.GetByCategory(FredSeriesCategory.Employment).ToList().Should().ContainSingle();
        _repository.GetByCategory(FredSeriesCategory.GdpAndOutput).ToList().Should().ContainSingle();
        _repository.GetByCategory(FredSeriesCategory.Housing).ToList().Should().BeEmpty();
    }

    // ── Search ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_BySeriesId_FindsMatch() {
        _dbContext.Set<FredSeries>().AddRange(
            CreateSeries("DFF", "Federal Funds Effective Rate"),
            CreateSeries("DGS10", "10-Year Treasury Constant Maturity Rate")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("DFF").ToList();

        result.Should().ContainSingle()
            .Which.SeriesId.Should().Be("DFF");
    }

    [Fact]
    public async Task Search_ByTitle_FindsMatch() {
        _dbContext.Set<FredSeries>().AddRange(
            CreateSeries("DFF", "Federal Funds Effective Rate"),
            CreateSeries("DGS10", "10-Year Treasury Constant Maturity Rate")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("Treasury").ToList();

        result.Should().ContainSingle()
            .Which.SeriesId.Should().Be("DGS10");
    }

    [Fact]
    public async Task Search_CaseInsensitive_FindsMatch() {
        _dbContext.Set<FredSeries>().Add(
            CreateSeries("DFF", "Federal Funds Effective Rate")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("federal funds").ToList();

        result.Should().ContainSingle()
            .Which.SeriesId.Should().Be("DFF");
    }

    [Fact]
    public async Task Search_PartialMatch_FindsMatch() {
        _dbContext.Set<FredSeries>().Add(
            CreateSeries("CPIAUCSL", "CPI for All Urban Consumers")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("CPI").ToList();

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty() {
        _dbContext.Set<FredSeries>().Add(
            CreateSeries("DFF", "Federal Funds Effective Rate")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("NONEXISTENT").ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_MatchesMultipleSeries_ReturnsAll() {
        _dbContext.Set<FredSeries>().AddRange(
            CreateSeries("DFF", "Federal Funds Effective Rate"),
            CreateSeries("FEDFUNDS", "Federal Funds Rate Monthly")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("Federal").ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_MatchesBothSeriesIdAndTitle_ReturnsAll() {
        _dbContext.Set<FredSeries>().AddRange(
            CreateSeries("RATE", "Some Obscure Rate"),
            CreateSeries("DFF", "Federal Funds RATE")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.Search("RATE").ToList();

        result.Should().HaveCount(2);
    }
}

public class FredObservationRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly FredObservationRepository _repository;

    public FredObservationRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new FredModuleConfiguration());
        _repository = new FredObservationRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static FredSeries CreateSeries(
        string seriesId = "DFF",
        string title = "Federal Funds Effective Rate") {
        return new FredSeries {
            Id = Guid.NewGuid(),
            SeriesId = seriesId,
            Title = title,
            Category = FredSeriesCategory.InterestRates,
            Frequency = "Daily",
            Units = "Percent",
            SeasonalAdjustment = "Not Seasonally Adjusted",
        };
    }

    private static FredObservation CreateObservation(
        Guid seriesId,
        DateOnly date,
        decimal? value = 5.33m) {
        return new FredObservation {
            Id = Guid.NewGuid(),
            FredSeriesId = seriesId,
            Date = date,
            Value = value,
        };
    }

    // ── GetBySeries ────────────────────────────────────────────────────

    [Fact]
    public async Task GetBySeries_ReturnsObservationsForSeries() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(series.Id, new DateOnly(2024, 1, 1), 5.33m),
            CreateObservation(series.Id, new DateOnly(2024, 1, 2), 5.34m),
            CreateObservation(series.Id, new DateOnly(2024, 1, 3), 5.35m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetBySeries(series).ToList();

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(o => o.FredSeriesId.Should().Be(series.Id));
    }

    [Fact]
    public async Task GetBySeries_DoesNotReturnOtherSeriesObservations() {
        var seriesA = CreateSeries("DFF", "Federal Funds");
        var seriesB = CreateSeries("DGS10", "10Y Treasury");
        _dbContext.Set<FredSeries>().AddRange(seriesA, seriesB);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(seriesA.Id, new DateOnly(2024, 1, 1), 5.33m),
            CreateObservation(seriesB.Id, new DateOnly(2024, 1, 1), 4.20m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetBySeries(seriesA).ToList();

        result.Should().ContainSingle()
            .Which.Value.Should().Be(5.33m);
    }

    [Fact]
    public async Task GetBySeries_EmptyObservations_ReturnsEmpty() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetBySeries(series).ToList();

        result.Should().BeEmpty();
    }

    // ── GetBySeries (date range) ───────────────────────────────────────

    [Fact]
    public async Task GetBySeries_WithDateRange_FiltersCorrectly() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(series.Id, new DateOnly(2024, 1, 1), 5.30m),
            CreateObservation(series.Id, new DateOnly(2024, 1, 15), 5.33m),
            CreateObservation(series.Id, new DateOnly(2024, 2, 1), 5.35m),
            CreateObservation(series.Id, new DateOnly(2024, 3, 1), 5.40m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetBySeries(
            series,
            new DateOnly(2024, 1, 10),
            new DateOnly(2024, 2, 15)
        ).ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(o => o.Date == new DateOnly(2024, 1, 15));
        result.Should().Contain(o => o.Date == new DateOnly(2024, 2, 1));
    }

    [Fact]
    public async Task GetBySeries_DateRangeInclusive_IncludesBoundaryDates() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(series.Id, new DateOnly(2024, 1, 1), 5.30m),
            CreateObservation(series.Id, new DateOnly(2024, 1, 15), 5.33m),
            CreateObservation(series.Id, new DateOnly(2024, 1, 31), 5.35m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetBySeries(
            series,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31)
        ).ToList();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetBySeries_DateRangeExcludesOtherSeries() {
        var seriesA = CreateSeries("DFF", "Federal Funds");
        var seriesB = CreateSeries("DGS10", "10Y Treasury");
        _dbContext.Set<FredSeries>().AddRange(seriesA, seriesB);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(seriesA.Id, new DateOnly(2024, 1, 15), 5.33m),
            CreateObservation(seriesB.Id, new DateOnly(2024, 1, 15), 4.20m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetBySeries(
            seriesA,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31)
        ).ToList();

        result.Should().ContainSingle()
            .Which.Value.Should().Be(5.33m);
    }

    [Fact]
    public async Task GetBySeries_DateRangeNoMatches_ReturnsEmpty() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.Set<FredObservation>().Add(
            CreateObservation(series.Id, new DateOnly(2024, 6, 1), 5.33m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetBySeries(
            series,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31)
        ).ToList();

        result.Should().BeEmpty();
    }

    // ── GetLatestDate ──────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestDate_ReturnsMostRecentDate() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(series.Id, new DateOnly(2024, 1, 1), 5.30m),
            CreateObservation(series.Id, new DateOnly(2024, 6, 15), 5.33m),
            CreateObservation(series.Id, new DateOnly(2024, 3, 1), 5.35m)
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate(series).FirstOrDefaultAsync();

        result.Should().Be(new DateOnly(2024, 6, 15));
    }

    [Fact]
    public async Task GetLatestDate_NoObservations_ReturnsDefault() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate(series).FirstOrDefaultAsync();

        result.Should().Be(default(DateOnly));
    }

    [Fact]
    public async Task GetLatestDate_DoesNotReturnOtherSeriesDates() {
        var seriesA = CreateSeries("DFF", "Federal Funds");
        var seriesB = CreateSeries("DGS10", "10Y Treasury");
        _dbContext.Set<FredSeries>().AddRange(seriesA, seriesB);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(seriesA.Id, new DateOnly(2024, 1, 1), 5.30m),
            CreateObservation(seriesB.Id, new DateOnly(2024, 12, 31), 4.20m)
        );
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetLatestDate(seriesA).FirstOrDefaultAsync();

        result.Should().Be(new DateOnly(2024, 1, 1));
    }

    // ── GetLatestPerSeries ─────────────────────────────────────────────

    [Fact]
    public async Task GetLatestPerSeries_ReturnsMostRecentPerSeries() {
        var seriesA = CreateSeries("DFF", "Federal Funds");
        var seriesB = CreateSeries("DGS10", "10Y Treasury");
        _dbContext.Set<FredSeries>().AddRange(seriesA, seriesB);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(seriesA.Id, new DateOnly(2024, 1, 1), 5.30m),
            CreateObservation(seriesA.Id, new DateOnly(2024, 6, 1), 5.33m),
            CreateObservation(seriesB.Id, new DateOnly(2024, 3, 1), 4.10m),
            CreateObservation(seriesB.Id, new DateOnly(2024, 9, 1), 4.20m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerSeries().ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(o => o.FredSeriesId == seriesA.Id && o.Date == new DateOnly(2024, 6, 1));
        result.Should().Contain(o => o.FredSeriesId == seriesB.Id && o.Date == new DateOnly(2024, 9, 1));
    }

    [Fact]
    public async Task GetLatestPerSeries_ExcludesNullValues() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(series.Id, new DateOnly(2024, 1, 1), 5.30m),
            CreateObservation(series.Id, new DateOnly(2024, 6, 1), null)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerSeries().ToList();

        result.Should().ContainSingle()
            .Which.Date.Should().Be(new DateOnly(2024, 1, 1));
    }

    [Fact]
    public async Task GetLatestPerSeries_AllNullValues_ReturnsEmpty() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.Set<FredObservation>().AddRange(
            CreateObservation(series.Id, new DateOnly(2024, 1, 1), null),
            CreateObservation(series.Id, new DateOnly(2024, 6, 1), null)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerSeries().ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestPerSeries_NoObservations_ReturnsEmpty() {
        var result = _repository.GetLatestPerSeries().ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestPerSeries_SingleSeriesSingleObservation_ReturnsThatObservation() {
        var series = CreateSeries();
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.Set<FredObservation>().Add(
            CreateObservation(series.Id, new DateOnly(2024, 3, 15), 5.33m)
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetLatestPerSeries().ToList();

        result.Should().ContainSingle()
            .Which.Value.Should().Be(5.33m);
    }
}
