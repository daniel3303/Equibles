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
/// Pins <c>GetTopHolders</c> — the third of four InstitutionalHoldingsTools.
/// The tool ranks institutions by Shares descending and renders the
/// "% of Total" column relative to the overall sum across all holders for
/// the target stock and report date. The most realistic failure modes are
/// (a) ranking by Value instead of Shares (would silently reorder rows on
/// every quarter where a large-share/low-value holder exists) and (b)
/// computing the percentage against a single row's shares instead of the
/// total (would emit 100% for every row). Both are caught here.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetTopHoldersTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetTopHoldersTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetTopHolders_TwoHoldersWithLargeRatio_RanksByShareCountWithCorrectPercentages()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var bigHolder = new InstitutionalHolder { Cik = "1", Name = "Vanguard Group Inc." };
        var smallHolder = new InstitutionalHolder { Cik = "2", Name = "Tiny Capital LLC" };
        DbContext.Add(stock);
        DbContext.Add(bigHolder);
        DbContext.Add(smallHolder);

        var reportDate = new DateOnly(2024, 12, 31);
        // 9,000 + 1,000 = 10,000 total → Vanguard = 90%, Tiny = 10%.
        DbContext.Add(MakeHolding(stock, bigHolder, reportDate, shares: 9_000));
        DbContext.Add(MakeHolding(stock, smallHolder, reportDate, shares: 1_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopHolders("AAPL");

        // Vanguard ranks first by Shares with 90% of total.
        var vanguardIdx = output.IndexOf("Vanguard Group Inc.", StringComparison.Ordinal);
        var tinyIdx = output.IndexOf("Tiny Capital LLC", StringComparison.Ordinal);
        vanguardIdx.Should().BeGreaterThan(0);
        tinyIdx.Should().BeGreaterThan(vanguardIdx, "ranking must be by Shares descending");
        output.Should().Contain("90.00%");
        output.Should().Contain("10.00%");
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}",
        };
}
