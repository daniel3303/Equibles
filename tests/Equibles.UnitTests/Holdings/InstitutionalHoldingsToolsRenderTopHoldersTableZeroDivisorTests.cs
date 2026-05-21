using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderTopHoldersTableZeroDivisorTests
{
    private static readonly MethodInfo RenderTopHoldersTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderTopHoldersTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderTopHoldersTable (extracted in #1561) computes per-row "% of Total"
    // as shares / totalSharesAll. A holdings set composed entirely of option
    // rows or other zero-share entries makes totalSharesAll == 0; without a
    // divide-by-zero guard, double-arithmetic emits NaN and the format spec
    // renders the literal "NaN" cell. The contract is that the table stays
    // numeric — a refactor that dropped the `totalSharesAll > 0` guard would
    // ship "NaN%" cells to the LLM consumer.
    [Fact]
    public void RenderTopHoldersTable_TotalSharesAllZero_RowsRenderWithoutNaN()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var holder = new InstitutionalHolder { Name = "Test Fund" };
        var holdings = new List<InstitutionalHolding>
        {
            new()
            {
                InstitutionalHolder = holder,
                Shares = 0,
                Value = 0,
            },
        };

        var rendered = (string)
            RenderTopHoldersTableMethod.Invoke(
                null,
                [stock, "AAPL", new DateOnly(2024, 9, 30), 1, 0L, 0L, holdings]
            );

        rendered.Should().NotContain("NaN");
    }
}
