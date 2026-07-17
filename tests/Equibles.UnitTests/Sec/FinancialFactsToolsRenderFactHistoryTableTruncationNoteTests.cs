using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// GetFinancialFact caps the table at maxResults but historically gave no
/// signal that more periods exist — an LLM consumer could not distinguish
/// "history starts here" from "truncated at the limit". The renderer must
/// append the shared truncation note when (and only when) rows were cut off.
/// </summary>
public class FinancialFactsToolsRenderFactHistoryTableTruncationNoteTests
{
    private static string Render(int shown, int total)
    {
        var method = typeof(FinancialFactsTools).GetMethod(
            "RenderFactHistoryTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var stock = new CommonStock { Ticker = "NVDA", Name = "NVIDIA Corp" };
        var perPeriod = Enumerable
            .Range(0, shown)
            .Select(i => new FinancialFact
            {
                Value = 1_000m + i,
                Unit = "USD",
                PeriodType = FactPeriodType.Duration,
                PeriodStart = new DateOnly(2020 + i, 1, 1),
                PeriodEnd = new DateOnly(2020 + i, 12, 31),
                FiscalYear = 2020 + i,
                FiscalPeriod = SecFiscalPeriod.FullYear,
                Form = DocumentType.TenK,
                FiledDate = new DateOnly(2021 + i, 2, 1),
                AccessionNumber = $"acc-{i}",
            })
            .ToList();

        return (string)method.Invoke(null, ["revenue", stock, false, perPeriod, total]);
    }

    [Fact]
    public void RenderFactHistoryTable_MorePeriodsThanShown_AppendsTruncationNote()
    {
        var result = Render(shown: 2, total: 5);

        result.Should().Contain("Showing first 2 of 5 results");
        result.Should().Contain("maxResults");
    }

    [Fact]
    public void RenderFactHistoryTable_NothingCutOff_NoTruncationNote()
    {
        var result = Render(shown: 2, total: 2);

        result.Should().NotContain("Showing first");
    }
}
