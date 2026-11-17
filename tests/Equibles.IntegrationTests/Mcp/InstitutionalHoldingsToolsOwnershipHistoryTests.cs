using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling to <see cref="InstitutionalHoldingsToolsSearchTests"/>. Pins
/// <c>GetInstitutionalOwnershipHistory</c> — the second-most complex of the four tools.
/// The percent-change column is computed against the prior quarter's total,
/// rendered with the production format string <c>+0.0;-0.0</c>. A regression
/// that swapped the format to a default <c>F1</c> would lose the sign prefix
/// (positive changes would render without a leading '+'), silently breaking
/// MCP clients that parse the column to detect ownership trend direction.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsOwnershipHistoryTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsOwnershipHistoryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionalOwnershipHistory_QuarterOverQuarterIncrease_RendersPlusSignedPercentChange()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        var holder = new InstitutionalHolder
        {
            Cik = "0001067983",
            Name = "Berkshire Hathaway Inc.",
        };
        DbContext.Add(stock);
        DbContext.Add(holder);

        // Q1: 1_000 shares total. Q2: 1_500 shares total → +50.0% change.
        // The format string +0.0;-0.0 must render the increase with a leading '+'.
        DbContext.Add(MakeHolding(stock, holder, new DateOnly(2024, 9, 30), shares: 1_000));
        DbContext.Add(MakeHolding(stock, holder, new DateOnly(2024, 12, 31), shares: 1_500));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetInstitutionalOwnershipHistory("AAPL");

        // First quarter has no prior — change column is "—".
        output.Should().Contain("2024-09-30");
        output.Should().Contain("| — |");
        // Second quarter must show "+50.0%" — not "50.0%" (no sign) or "+50%" (no decimal).
        output.Should().Contain("+50.0%");
    }

    [Fact]
    public async Task GetInstitutionalOwnershipHistory_HolderWithAWithheldQuarter_NamesItUnderConfidentialTreatment()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = await ToolsOverAWithheldQuarter(verify);

        var output = await sut.GetInstitutionalOwnershipHistory("ARE");

        output
            .Should()
            .Contain(
                "- 2026-03-31: Norges Bank (CIK 1374170) last reported 16,460 shares for 2025-12-31."
            );
        output.Should().Contain("2026-03-31 13F withheld under confidential treatment");
        // The total stays as filed: the withheld position is not carried into it.
        output.Should().Contain("| 2026-03-31 | 1 | 1,000 |");
    }

    [Fact]
    public async Task GetTopHolders_QuarterAHolderWithheld_NamesItBesideTheList()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = await ToolsOverAWithheldQuarter(verify);

        var withheldQuarter = await sut.GetTopHolders("ARE", "2026-03-31");
        var disclosedQuarter = await sut.GetTopHolders("ARE", "2025-12-31");

        withheldQuarter
            .Should()
            .Contain("- Norges Bank (CIK 1374170) last reported 16,460 shares for 2025-12-31.");
        withheldQuarter.Should().NotContain("| Norges Bank |");
        disclosedQuarter.Should().Contain("| Norges Bank |");
        disclosedQuarter.Should().NotContain("confidential treatment");
    }

    // Norges Bank holds the stock at 2025-12-31 and withholds its whole 2026-03-31 book.
    private async Task<InstitutionalHoldingsTools> ToolsOverAWithheldQuarter(
        Equibles.Data.EquiblesFinancialDbContext verify
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "ARE",
            Name: "Alexandria Real Estate Equities",
            Cik: "0001035443"
        );
        var steady = new InstitutionalHolder { Cik = "102909", Name = "Steady Filer" };
        var withholding = new InstitutionalHolder { Cik = "1374170", Name = "Norges Bank" };
        DbContext.AddRange(stock, steady, withholding);
        DbContext.Add(MakeHolding(stock, steady, new DateOnly(2025, 12, 31), shares: 1_000));
        DbContext.Add(MakeHolding(stock, steady, new DateOnly(2026, 3, 31), shares: 1_000));
        DbContext.Add(MakeHolding(stock, withholding, new DateOnly(2025, 12, 31), shares: 16_460));
        DbContext.Add(
            new Form13FConfidentialFiling
            {
                AccessionNumber = "0001374170-26-000023",
                FilerCik = withholding.Cik,
                ReportDate = new DateOnly(2026, 3, 31),
                FilingDate = new DateOnly(2026, 5, 11),
                DeclaredEntryTotal = 1507,
                DisclosedPositionCount = 0,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        return new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );
    }

    private static InstitutionalHolding MakeHolding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
