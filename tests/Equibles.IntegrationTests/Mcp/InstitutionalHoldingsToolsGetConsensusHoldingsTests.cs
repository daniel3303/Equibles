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
/// Pins <c>GetConsensusHoldings</c>. Each test exercises one path: too-few-names guard,
/// no-common-quarter, multi-fund consensus ordering, and the <c>minFunds</c> filter.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetConsensusHoldingsTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetConsensusHoldingsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetConsensusHoldings_OnlyOneName_ReportsTooFew()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetConsensusHoldings("Solo Fund");

        output.Should().Contain("at least two institution names");
    }

    [Fact]
    public async Task GetConsensusHoldings_ThreeFunds_RanksConsensusFirst()
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
        var fundA = new InstitutionalHolder { Cik = "CH00001", Name = "Consensus A LP" };
        var fundB = new InstitutionalHolder { Cik = "CH00002", Name = "Consensus B LP" };
        var fundC = new InstitutionalHolder { Cik = "CH00003", Name = "Consensus C LP" };
        DbContext.AddRange(aapl, msft, nvda, fundA, fundB, fundC);
        var date = new DateOnly(2024, 12, 31);
        DbContext.Add(MakeHolding(aapl, fundA, date, value: 1_000_000));
        DbContext.Add(MakeHolding(aapl, fundB, date, value: 1_500_000));
        DbContext.Add(MakeHolding(aapl, fundC, date, value: 2_000_000));
        DbContext.Add(MakeHolding(msft, fundA, date, value: 500_000));
        DbContext.Add(MakeHolding(msft, fundB, date, value: 700_000));
        DbContext.Add(MakeHolding(nvda, fundC, date, value: 800_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetConsensusHoldings("Consensus A, Consensus B, Consensus C");

        output.Should().Contain("Consensus holdings — **3 funds**");
        output.Should().Contain("AAPL");
        output.Should().Contain("MSFT");
        output.Should().Contain("NVDA");
        output
            .IndexOf("AAPL", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("MSFT", StringComparison.Ordinal));
        output
            .IndexOf("MSFT", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("NVDA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetConsensusHoldings_MinFundsFilter_ExcludesBelowThreshold()
    {
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var nvda = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corp.",
            Cik = "0001045810",
        };
        var fundA = new InstitutionalHolder { Cik = "CH00004", Name = "Filter A LP" };
        var fundB = new InstitutionalHolder { Cik = "CH00005", Name = "Filter B LP" };
        DbContext.AddRange(aapl, nvda, fundA, fundB);
        var date = new DateOnly(2024, 12, 31);
        // AAPL held by both. NVDA only by Fund B.
        DbContext.Add(MakeHolding(aapl, fundA, date, value: 1_000_000));
        DbContext.Add(MakeHolding(aapl, fundB, date, value: 500_000));
        DbContext.Add(MakeHolding(nvda, fundB, date, value: 300_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetConsensusHoldings("Filter A, Filter B", minFunds: 2);

        output.Should().Contain("AAPL");
        output.Should().NotContain("NVDA");
    }

    [Fact]
    public async Task GetConsensusHoldings_NoCommonQuarter_ReportsNoOverlap()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var fundA = new InstitutionalHolder { Cik = "CH00006", Name = "Mismatch A LP" };
        var fundB = new InstitutionalHolder { Cik = "CH00007", Name = "Mismatch B LP" };
        DbContext.AddRange(stock, fundA, fundB);
        DbContext.Add(MakeHolding(stock, fundA, new DateOnly(2024, 3, 31), value: 100_000));
        DbContext.Add(MakeHolding(stock, fundB, new DateOnly(2024, 6, 30), value: 100_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetConsensusHoldings("Mismatch A, Mismatch B");

        output.Should().Contain("share no common report dates");
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesDbContext ctx) =>
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
