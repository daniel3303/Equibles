using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins <c>GetInstitutionSummary</c>. Resolves the holder via the name-search query,
/// pulls current + prior quarter holdings, and feeds them to the same
/// <see cref="InstitutionPortfolioSummaryCalculator"/> the web profile uses. Each
/// <see cref="Fact"/> exercises one path so a regression in lookup or calculation
/// surfaces as a focused assertion.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionSummaryTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionSummaryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionSummary_UnknownInstitution_ReportsNotFound()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Definitely Not A Fund");

        output.Should().Contain("No institution found");
    }

    [Fact]
    public async Task GetInstitutionSummary_HolderWithNoHoldings_ReportsNoData()
    {
        DbContext.Add(new InstitutionalHolder { Cik = "00010001", Name = "Brand New Capital LP" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Brand New");

        output.Should().Contain("No 13F holdings reported by Brand New Capital LP");
    }

    [Fact]
    public async Task GetInstitutionSummary_TwoQuarterHolder_RendersAllMetricsAndCaption()
    {
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var msft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        var holder = new InstitutionalHolder { Cik = "00010002", Name = "Big Fund LP" };
        DbContext.AddRange(aapl, msft, holder);
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        // Prior + current with movement on AAPL.
        DbContext.Add(MakeHolding(aapl, holder, prior, shares: 1_000, value: 1_000_000));
        DbContext.Add(MakeHolding(msft, holder, prior, shares: 500, value: 500_000));
        DbContext.Add(MakeHolding(aapl, holder, current, shares: 1_500, value: 1_500_000));
        DbContext.Add(MakeHolding(msft, holder, current, shares: 500, value: 500_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Big Fund");

        output.Should().Contain("Portfolio summary — **Big Fund LP** as of 2024-12-31");
        output.Should().Contain("vs prior quarter 2024-09-30");
        output.Should().Contain("Reported AUM");
        output.Should().Contain("# Positions");
        output.Should().Contain("Top 10 concentration");
        output.Should().Contain("Top 25 concentration");
        output.Should().Contain("QoQ turnover");
        output.Should().Contain("Quarters reported");
        output.Should().Contain("_QoQ turnover = (");
    }

    [Fact]
    public async Task GetInstitutionSummary_ExplicitReportDate_HonorsArgumentWhenItMatches()
    {
        var stock = new CommonStock
        {
            Ticker = "TSLA",
            Name = "Tesla Inc.",
            Cik = "0001318605",
        };
        var holder = new InstitutionalHolder { Cik = "00010003", Name = "Targeted Capital" };
        DbContext.AddRange(stock, holder);
        var q3 = new DateOnly(2024, 9, 30);
        var q4 = new DateOnly(2024, 12, 31);
        DbContext.Add(MakeHolding(stock, holder, q3, shares: 100, value: 100_000));
        DbContext.Add(MakeHolding(stock, holder, q4, shares: 500, value: 500_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Targeted Capital", reportDate: "2024-09-30");

        output.Should().Contain("as of 2024-09-30");
        // No prior quarter further back, so the "vs prior" line MUST be absent.
        output.Should().NotContain("vs prior quarter");
    }

    [Fact]
    public async Task GetInstitutionSummary_AmbiguousName_PrefersShortestMatch()
    {
        // "BlackRock" matches both "BlackRock, Inc." (parent) and
        // "BlackRock Advisors LLC" (subsidiary). The parent is the intended
        // match — it has the shorter name and is the primary 13F filer.
        DbContext.AddRange(
            new InstitutionalHolder { Cik = "00080001", Name = "BlackRock Advisors LLC" },
            new InstitutionalHolder { Cik = "00080002", Name = "BlackRock, Inc." }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("BlackRock");

        output.Should().Contain("BlackRock, Inc.");
        output.Should().NotContain("BlackRock Advisors");
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesFinancialDbContext ctx) =>
        new(
            new InstitutionalHoldingRepository(ctx),
            new InstitutionalHolderRepository(ctx),
            new CommonStockRepository(ctx),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
