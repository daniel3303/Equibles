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
/// Pins <c>GetFundOverlap</c>. Each test exercises one path: unknown lookups on either
/// side, no-common-quarter, two-fund partial overlap, and the explicit reportDate
/// argument.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetFundOverlapTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetFundOverlapTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetFundOverlap_UnknownFirstInstitution_ReportsNotFound()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetFundOverlap("Nobody", "Anybody");

        output.Should().Contain("No institution found matching 'Nobody'");
    }

    [Fact]
    public async Task GetFundOverlap_NoCommonQuarter_ReportsNoOverlap()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var fundA = new InstitutionalHolder { Cik = "FO00001", Name = "Fund A LP" };
        var fundB = new InstitutionalHolder { Cik = "FO00002", Name = "Fund B LP" };
        DbContext.AddRange(stock, fundA, fundB);
        DbContext.Add(MakeHolding(stock, fundA, new DateOnly(2024, 3, 31), value: 100_000));
        DbContext.Add(MakeHolding(stock, fundB, new DateOnly(2024, 6, 30), value: 100_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetFundOverlap("Fund A", "Fund B");

        output.Should().Contain("share no common report dates");
    }

    [Fact]
    public async Task GetFundOverlap_TwoFundsWithPartialOverlap_RendersStatsAndTable()
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
        var nvda = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corp.",
            Cik = "0001045810",
        };
        var fundA = new InstitutionalHolder { Cik = "FO00003", Name = "Overlap A LP" };
        var fundB = new InstitutionalHolder { Cik = "FO00004", Name = "Overlap B LP" };
        DbContext.AddRange(aapl, msft, nvda, fundA, fundB);
        var date = new DateOnly(2024, 12, 31);
        // Fund A: AAPL + MSFT. Fund B: AAPL + NVDA. Union = 3, intersection = 1 (AAPL).
        DbContext.Add(MakeHolding(aapl, fundA, date, value: 1_000_000));
        DbContext.Add(MakeHolding(msft, fundA, date, value: 500_000));
        DbContext.Add(MakeHolding(aapl, fundB, date, value: 800_000));
        DbContext.Add(MakeHolding(nvda, fundB, date, value: 300_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetFundOverlap("Overlap A", "Overlap B");

        output.Should().Contain("Portfolio overlap — **Overlap A LP** vs **Overlap B LP**");
        output.Should().Contain("Union positions");
        output.Should().Contain("Shared positions");
        output.Should().Contain("Jaccard similarity");
        // Each of the three tickers appears in the side-by-side table.
        output.Should().Contain("AAPL");
        output.Should().Contain("MSFT");
        output.Should().Contain("NVDA");
    }

    [Fact]
    public async Task GetFundOverlap_ExplicitReportDate_HonorsArgument()
    {
        var stock = new CommonStock
        {
            Ticker = "TSLA",
            Name = "Tesla Inc.",
            Cik = "0001318605",
        };
        var fundA = new InstitutionalHolder { Cik = "FO00005", Name = "Dated A LP" };
        var fundB = new InstitutionalHolder { Cik = "FO00006", Name = "Dated B LP" };
        DbContext.AddRange(stock, fundA, fundB);
        var q3 = new DateOnly(2024, 9, 30);
        var q4 = new DateOnly(2024, 12, 31);
        // Both funds report Q3 and Q4.
        DbContext.Add(MakeHolding(stock, fundA, q3, value: 100_000));
        DbContext.Add(MakeHolding(stock, fundA, q4, value: 500_000));
        DbContext.Add(MakeHolding(stock, fundB, q3, value: 200_000));
        DbContext.Add(MakeHolding(stock, fundB, q4, value: 700_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetFundOverlap("Dated A LP", "Dated B LP", reportDate: "2024-09-30");

        output.Should().Contain("as of 2024-09-30");
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
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
