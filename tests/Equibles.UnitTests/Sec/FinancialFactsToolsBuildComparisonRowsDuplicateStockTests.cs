using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// A company's primary and secondary tickers (GOOGL/GOOG) resolve to the same
/// CommonStock, and BuildComparisonRows historically added one row per
/// requested string — 'GOOGL,GOOG' produced two identical GOOGL rows, and a
/// secondary-only request came back labeled with a ticker the caller never
/// asked for. One row per company; repeats are reported in the skip list; a
/// secondary-ticker row stays traceable to the caller's input.
/// </summary>
public class FinancialFactsToolsBuildComparisonRowsDuplicateStockTests
{
    private static readonly Guid AlphabetId = Guid.NewGuid();

    private static CommonStock Alphabet() =>
        new()
        {
            Id = AlphabetId,
            Ticker = "GOOGL",
            Name = "Alphabet Inc.",
            SecondaryTickers = ["GOOG"],
        };

    private static FinancialFact Fact() =>
        new()
        {
            CommonStockId = AlphabetId,
            Value = 307_394_000_000m,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2023, 1, 1),
            PeriodEnd = new DateOnly(2023, 12, 31),
            FiscalYear = 2023,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = new DateOnly(2024, 1, 30),
            AccessionNumber = "acc-goog",
        };

    private static (
        List<(string Ticker, string Name, FinancialFact Fact)> Rows,
        List<string> Skipped
    ) Invoke(List<string> requested, Dictionary<string, CommonStock> stockByTicker)
    {
        var method = typeof(FinancialFactsTools).GetMethod(
            "BuildComparisonRows",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var bestByStock = new Dictionary<Guid, FinancialFact> { [AlphabetId] = Fact() };
        return ((List<(string Ticker, string Name, FinancialFact Fact)>, List<string>))
            method.Invoke(null, [requested, stockByTicker, bestByStock]);
    }

    [Fact]
    public void BuildComparisonRows_PrimaryAndSecondaryTickerOfSameStock_OneRowPlusDuplicateNotice()
    {
        var stock = Alphabet();
        var stockByTicker = new Dictionary<string, CommonStock>
        {
            ["GOOGL"] = stock,
            ["GOOG"] = stock,
        };

        var (rows, skipped) = Invoke(["GOOGL", "GOOG"], stockByTicker);

        rows.Should().HaveCount(1, "one company gets one comparison row");
        rows[0].Ticker.Should().Be("GOOGL");
        skipped.Should().ContainSingle(s => s.Contains("GOOG (same company as GOOGL)"));
    }

    [Fact]
    public void BuildComparisonRows_SecondaryTickerOnly_RowTraceableToRequestedTicker()
    {
        var stock = Alphabet();
        var stockByTicker = new Dictionary<string, CommonStock> { ["GOOG"] = stock };

        var (rows, skipped) = Invoke(["GOOG"], stockByTicker);

        rows.Should().HaveCount(1);
        rows[0].Ticker.Should().Be("GOOG (GOOGL)", "the caller asked for GOOG");
        skipped.Should().BeEmpty();
    }
}
