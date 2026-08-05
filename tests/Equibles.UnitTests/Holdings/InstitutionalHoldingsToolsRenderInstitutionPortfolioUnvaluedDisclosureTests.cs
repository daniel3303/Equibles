using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the unvalued-position disclosure: rows a filing tracks but the platform could not price
/// sit in the table at $0, and the header must say so — attributing the whole declared-vs-tracked
/// gap to "security types outside coverage" once hid a $92.1B valuation hole (48% of a quarter's
/// positions) behind a coverage excuse.
/// </summary>
public class InstitutionalHoldingsToolsRenderInstitutionPortfolioUnvaluedDisclosureTests
{
    private static string Render(int unvaluedPositions, InstitutionalFiling declaringFiling)
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderInstitutionPortfolio",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var holder = new InstitutionalHolder { Name = "ACME Capital", Cik = "0001234567" };
        var stock = new CommonStock { Ticker = "NVDA", Name = "Nvidia Corp" };
        var holdings = new List<InstitutionalHolding>
        {
            new()
            {
                CommonStock = stock,
                Shares = 1_000,
                Value = 5_000_000L,
            },
        };

        return (string)
            method.Invoke(
                null,
                [
                    holder,
                    new DateOnly(2026, 3, 31),
                    holdings,
                    new Dictionary<Guid, List<StockSplit>>(),
                    holdings.Count,
                    holdings.Sum(h => h.Value),
                    unvaluedPositions,
                    declaringFiling,
                    null,
                ]
            );
    }

    [Fact]
    public void RenderInstitutionPortfolio_UnvaluedPositions_AreDisclosedWithACount()
    {
        var output = Render(unvaluedPositions: 507, declaringFiling: null);

        output.Should().Contain("507 tracked position(s) have no derivable value");
        output.Should().Contain("understate the filing");
    }

    [Fact]
    public void RenderInstitutionPortfolio_NoUnvaluedPositions_SaysNothingAboutThem()
    {
        var output = Render(unvaluedPositions: 0, declaringFiling: null);

        output.Should().NotContain("no derivable value");
    }

    [Fact]
    public void RenderInstitutionPortfolio_DeclaredGapWithUnvaluedRows_NamesBothCauses()
    {
        var filing = new InstitutionalFiling
        {
            AccessionNumber = "0000919079-26-000006",
            DeclaredPositionCount = 1064,
            DeclaredTotalValue = 162_486_869_017L,
        };

        var output = Render(unvaluedPositions: 507, declaringFiling: filing);

        output
            .Should()
            .Contain(
                "security types outside this platform's coverage plus the unvalued positions",
                "blaming coverage alone once hid a valuation hole behind an excuse"
            );
    }

    [Fact]
    public void RenderInstitutionPortfolio_DeclaredGapWithoutUnvaluedRows_KeepsCoverageExplanation()
    {
        var filing = new InstitutionalFiling
        {
            AccessionNumber = "0000919079-26-000006",
            DeclaredPositionCount = 1064,
            DeclaredTotalValue = 162_486_869_017L,
        };

        var output = Render(unvaluedPositions: 0, declaringFiling: filing);

        output.Should().Contain("the difference is security types outside this platform's coverage.");
        output.Should().NotContain("plus the unvalued positions");
    }
}
