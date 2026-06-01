using System.Globalization;
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

[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetMarketWide13FActivityChurnCultureInvarianceTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetMarketWide13FActivityChurnCultureInvarianceTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    // The churn leaderboard (new-positions / sold-out-positions) renders the filer-count cell
    // as {count:N0} with the culture-implicit specifier, which honours the thread CurrentCulture.
    // The established repo contract (the InvariantCulture call sites: "MCP markdown must not fork
    // the separators by host locale") is byte-identical output on every host. A mega-cap can have
    // 1,000+ filers initiating in a quarter; under de-DE that renders 1.000, forking the response
    // — same bug class as #3013 / #3030 / #3035 / #3043 / #3047.
    [Fact]
    public async Task GetMarketWide13FActivity_ChurnUnderNonInvariantCulture_RendersFilerCountCultureInvariantly()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        DbContext.Add(aapl);

        // Anchor the prior quarter so the date resolution finds current vs prior.
        var anchor = new InstitutionalHolder { Cik = "anchor", Name = "Anchor Fund" };
        DbContext.Add(anchor);
        DbContext.Add(MakeHolding(aapl, anchor, prior));

        // 1,000 distinct filers initiating AAPL this quarter (present at current, absent at
        // prior) → NewFilerCount = 1,000, which must render as "1,000".
        for (var i = 0; i < 1_000; i++)
        {
            var holder = new InstitutionalHolder { Cik = $"new-{i}", Name = $"Fund {i}" };
            DbContext.Add(holder);
            DbContext.Add(MakeHolding(aapl, holder, current));
        }
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var previous = CultureInfo.CurrentCulture;
        string output;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            output = await sut.GetMarketWide13FActivity(bucket: "new-positions");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The filer-count cell (bare :N0) must render with en-US grouping on every host
        // locale; de-DE would produce 1.000.
        output.Should().Contain("| 1,000 |");
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
        DateOnly reportDate
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = 1_000,
            Value = 1_000_000,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
