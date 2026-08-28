using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.Mcp;

public class FundDirectoryToolsTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FundDirectoryTools _tools;

    public FundDirectoryToolsTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _tools = new FundDirectoryTools(
            new FundSeriesRepository(_dbContext),
            new NportFilingRepository(_dbContext),
            errorManager: null,
            NullLogger<FundDirectoryTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task SearchFunds_MoreMatchesThanMaxResults_ReportsTotalAndTruncationNote()
    {
        for (var i = 0; i < 3; i++)
            SeedSeries($"ACME FUND {i}", $"S00000{i}", netAssets: 1_000_000m * (i + 1));

        var result = await _tools.SearchFunds("Acme", maxResults: 2);

        result.Should().Contain("showing 2 of 3");
        result.Should().Contain("Showing first 2 of 3 results");
        // Largest by net assets first: fund 2 shown, fund 0 cut off.
        result.Should().Contain("ACME FUND 2");
        result.Should().NotContain("ACME FUND 0");
    }

    [Fact]
    public async Task SearchFunds_AllMatchesShown_OmitsTruncationNote()
    {
        SeedSeries("ACME FUND", "S000001");

        var result = await _tools.SearchFunds("Acme");

        result.Should().Contain("showing 1 of 1");
        result.Should().NotContain("raise maxResults");
    }

    [Fact]
    public async Task SearchFunds_GlossesRegistrationTypeCode()
    {
        SeedSeries("ACME CLOSED-END FUND", "S000001", fundType: "N-2");

        var result = await _tools.SearchFunds("Acme");

        result.Should().Contain("N-2 (closed-end fund)");
    }

    [Fact]
    public async Task SearchFunds_NoMatch_StatesTrackedDirectoryBoundary()
    {
        var result = await _tools.SearchFunds("Treasury");

        result
            .Should()
            .Contain("No match for 'Treasury' in the tracked Form NPORT-P fund directory");
        result.Should().Contain("Fixed-income-only series can be absent");
        result.Should().Contain("money market funds and small business investment companies");
        result.Should().Contain("coverage result, not evidence that the fund does not exist");
    }

    [Fact]
    public async Task SearchFunds_TokenOrderAndPunctuationDoNotHaveToMirrorStoredName()
    {
        SeedSeries("GRANTHAM, MAYO GLOBAL FUND", "S000010");
        SeedSeries("GRANTHAM VALUE FUND", "S000011");

        var result = await _tools.SearchFunds("Mayo Grantham");

        result.Should().Contain("GRANTHAM, MAYO GLOBAL FUND");
        result.Should().NotContain("GRANTHAM VALUE FUND");
    }

    [Fact]
    public async Task SearchFunds_NoAllTokenMatch_BroadensToAnyToken()
    {
        SeedSeries("GRANTHAM, MAYO GLOBAL FUND", "S000010");

        var result = await _tools.SearchFunds("Current Mayo Grantham");

        result.Should().Contain("GRANTHAM, MAYO GLOBAL FUND");
    }

    [Fact]
    public async Task SearchFunds_VerifiedShareClassAlias_ResolvesSecSeries()
    {
        SeedSeries("VANGUARD 500 INDEX FUND", "S000002839");
        SeedSeries("VOO STRATEGY FUND", "S000009999");

        var result = await _tools.SearchFunds("VOO");

        result.Should().Contain("VANGUARD 500 INDEX FUND");
        result.Should().Contain("vanguard-500-index-fund-s000002839");
        result.Should().NotContain("VOO STRATEGY FUND");
    }

    [Fact]
    public async Task SearchFunds_ExactStoredTicker_OutranksVerifiedAlias()
    {
        SeedSeries("VANGUARD 500 INDEX FUND", "S000002839");
        SeedSeries("EXACT VOO FUND", "S000009999", ticker: "VOO");

        var result = await _tools.SearchFunds("VOO");

        result.Should().Contain("EXACT VOO FUND");
        result.Should().NotContain("VANGUARD 500 INDEX FUND");
    }

    [Fact]
    public async Task SearchFunds_LabelsStoredAndReportedHoldingCountsSeparately()
    {
        SeedSeries("ACME BOND FUND", "S000001", reportedHoldingCount: 812);

        var result = await _tools.SearchFunds("Acme");

        result.Should().Contain("| Stored | Reported |");
        result.Should().Contain("| 1 | 812 |");
        result.Should().Contain("only positions whose CUSIPs match tracked stocks");
    }

    [Fact]
    public async Task GetFundProfile_GlossesUnitAndCategoryCodesAndDropsWholeShareDecimals()
    {
        var series = SeedSeries("ACME FUND", "S000001");
        var filing = MakeFiling(series, "acc-1", new DateOnly(2026, 5, 15));
        filing.Holdings.Add(MakeHolding("BIG CO", 5_000_000m));
        _dbContext.Set<NportFiling>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundProfile(series.Slug);

        result.Should().Contain("NS (shares)");
        result.Should().Contain("EC (equity-common)");
        result.Should().Contain("| 5,000,000 |", "whole share balances carry no .00 noise");
    }

    [Fact]
    public async Task GetFundProfile_MoreHoldingsThanMaxResults_AppendsTruncationNote()
    {
        var series = SeedSeries("ACME FUND", "S000001");
        var filing = MakeFiling(series, "acc-1", new DateOnly(2026, 5, 15));
        for (var i = 0; i < 5; i++)
            filing.Holdings.Add(MakeHolding($"CO {i}", 1_000m * (i + 1)));
        _dbContext.Set<NportFiling>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundProfile(series.Slug, maxResults: 2);

        result.Should().Contain("showing the largest 2");
        result.Should().Contain("Showing first 2 of 5 results");
    }

    [Fact]
    public async Task GetFundProfile_SamePeriodAmendment_UsesNewestFilingDeterministically()
    {
        var series = SeedSeries("ACME FUND", "S000001");
        var original = MakeFiling(series, "acc-1", new DateOnly(2026, 5, 15));
        original.Holdings.Add(MakeHolding("ORIGINAL CO", 1_000m));
        var amendment = MakeFiling(series, "acc-2", new DateOnly(2026, 5, 20));
        amendment.IsAmendment = true;
        amendment.Holdings.Add(MakeHolding("AMENDED CO", 2_000m));
        _dbContext.AddRange(original, amendment);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundProfile(series.Slug);

        result.Should().Contain("AMENDED CO");
        result.Should().NotContain("ORIGINAL CO");
    }

    [Fact]
    public async Task GetFundProfile_NoStoredReport_StatesDatasetCoverageBoundary()
    {
        var series = SeedSeries("ACME FUND", "S000001");

        var result = await _tools.GetFundProfile(series.Slug);

        result.Should().Contain("No stored Form NPORT-P report is on record for ACME FUND");
        result.Should().Contain("dataset coverage result, not evidence that no SEC filing exists");
    }

    [Fact]
    public async Task GetFundProfile_FlattensAndEscapesFiledMarkdownBoundaries()
    {
        var series = SeedSeries("ACME FUND", "S000001");
        series.SeriesName = "ACME\n# SYNTHETIC | FUND";
        series.RegistrantName = "ACME\r\nTRUST | EXTRA";
        var filing = MakeFiling(series, "acc-1", new DateOnly(2026, 5, 15));
        var holding = MakeHolding("BIG\n# ROW | CO", 5_000_000m);
        holding.Cusip = "12|34";
        holding.Units = "N|S\nEXTRA";
        holding.AssetCategory = "E|C\nEXTRA";
        holding.InvestmentCountry = "U|S\nEXTRA";
        filing.Holdings.Add(holding);
        _dbContext.Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundProfile(series.Slug);

        result.Should().Contain("ACME # SYNTHETIC \\| FUND");
        result.Should().Contain("ACME  TRUST \\| EXTRA");
        result.Should().Contain("BIG # ROW \\| CO");
        result.Should().Contain("12\\|34");
        result.Should().Contain("N\\|S EXTRA");
        result.Should().Contain("E\\|C EXTRA");
        result.Should().Contain("U\\|S EXTRA");
        result.Should().NotContain("\n# SYNTHETIC");
        result.Should().NotContain("\n# ROW");
    }

    [Fact]
    public async Task GetFundProfile_VerifiedShareClassAlias_UsesDirectoryResolutionAndLabelsCoverage()
    {
        var series = SeedSeries("VANGUARD 500 INDEX FUND", "S000002839", reportedHoldingCount: 507);
        var filing = MakeFiling(series, "acc-1", new DateOnly(2026, 5, 15));
        filing.ReportedHoldingCount = 507;
        filing.Holdings.Add(MakeHolding("TRACKED CO", 5_000_000m));
        _dbContext.Set<NportFiling>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundProfile("VOO");

        result.Should().Contain("VANGUARD 500 INDEX FUND");
        result.Should().Contain("507 holdings reported, 1 stored tracked-stock holdings");
    }

    [Fact]
    public async Task GetFundProfile_UnknownFund_PointsAtSearchFundsAndStatesScope()
    {
        var result = await _tools.GetFundProfile("NOTAFUND");

        result.Should().Contain("No registered fund found for 'NOTAFUND'");
        result.Should().Contain("SearchFunds");
        result.Should().Contain("Fixed-income-only series");
    }

    private FundSeries SeedSeries(
        string name,
        string seriesId,
        decimal netAssets = 1_000_000m,
        string fundType = null,
        string ticker = null,
        int? reportedHoldingCount = 12
    )
    {
        var series = new FundSeries
        {
            LatestNportFilingId = Guid.NewGuid(),
            IdentityKey = $"rc:0000999999:{seriesId}",
            Slug = $"{name.ToLower().Replace(' ', '-').Replace("--", "-")}-{seriesId.ToLower()}",
            RegistrantCik = "0000999999",
            SeriesId = seriesId,
            SeriesName = name,
            Ticker = ticker,
            RegistrantName = "ACME TRUST",
            LatestReportPeriodDate = new DateOnly(2026, 3, 31),
            LatestFilingDate = new DateOnly(2026, 5, 15),
            NetAssets = netAssets,
            TotalAssets = netAssets,
            PositionCount = 1,
            ReportedHoldingCount = reportedHoldingCount,
            FundType = fundType,
        };
        _dbContext.Set<FundSeries>().Add(series);
        _dbContext.SaveChanges();
        return series;
    }

    private static NportFiling MakeFiling(FundSeries series, string accession, DateOnly filingDate)
    {
        return new NportFiling
        {
            CommonStockId = null,
            RegistrantCik = series.RegistrantCik,
            AccessionNumber = accession,
            FilingDate = filingDate,
            IsAmendment = false,
            RegistrantName = series.RegistrantName,
            SeriesName = series.SeriesName,
            SeriesId = series.SeriesId,
            ReportPeriodDate = new DateOnly(2026, 3, 31),
            ReportPeriodEnd = filingDate,
            TotalAssets = 1_200_000_000m,
            TotalLiabilities = 50_000_000m,
            NetAssets = 1_150_000_000m,
            ReportedHoldingCount = series.ReportedHoldingCount,
        };
    }

    private static NportHolding MakeHolding(string name, decimal valueUsd)
    {
        return new NportHolding
        {
            Name = name,
            Balance = valueUsd,
            Units = "NS",
            Currency = "USD",
            ValueUsd = valueUsd,
            PercentValue = 1.0m,
            PayoffProfile = "Long",
            AssetCategory = "EC",
            IssuerCategory = "CORP",
            InvestmentCountry = "US",
        };
    }
}
