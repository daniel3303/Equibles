using System.Text;
using BenchmarkDotNet.Attributes;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-filing parsing cost for the House/Senate disclosure HTML pages.
/// <see cref="DisclosureParsingHelper.ParseTransactionsFromHtml"/> drives the
/// congressional-trade scraper — every filing returned by the scraper passes
/// through it exactly once. The helper loads HtmlAgilityPack, walks every
/// table, and runs regex extraction per row, so its allocation profile shows
/// up directly in worker memory pressure over the multi-thousand-filing
/// backlog the scraper churns through on a cold start.
/// </summary>
[MemoryDiagnoser]
public class DisclosureParsingHelperBenchmarks
{
    private const int RowCount = 40;

    private string _html;

    [GlobalSetup]
    public void Setup()
    {
        // Build a Senate-shaped disclosure page: one table with the headers the
        // helper looks for, then N rows alternating between purchase / sale, with
        // realistic ticker/amount/asset values. The empty-sentinel "--" is included
        // on a few rows so the CleanSentinel path runs.
        var html = new StringBuilder();
        html.Append("<html><body><table>")
            .Append("<thead><tr>")
            .Append("<th>Transaction Date</th>")
            .Append("<th>Owner</th>")
            .Append("<th>Ticker</th>")
            .Append("<th>Asset Name</th>")
            .Append("<th>Asset Type</th>")
            .Append("<th>Transaction Type</th>")
            .Append("<th>Amount</th>")
            .Append("</tr></thead><tbody>");
        for (var i = 0; i < RowCount; i++)
        {
            var date = new DateOnly(2025, 1 + (i % 12), 1 + (i % 27));
            var type = i % 2 == 0 ? "Purchase" : "Sale (Full)";
            var owner = i % 3 == 0 ? "--" : "Self";
            var ticker = i % 7 == 0 ? "--" : $"AAPL";
            var asset = $"Apple Inc. (AAPL) - Common Stock #{i}";
            html.Append("<tr>")
                .Append($"<td>{date:MM/dd/yyyy}</td>")
                .Append($"<td>{owner}</td>")
                .Append($"<td>{ticker}</td>")
                .Append($"<td>{asset}</td>")
                .Append("<td>Stock</td>")
                .Append($"<td>{type}</td>")
                .Append("<td>$1,001 - $15,000</td>")
                .Append("</tr>");
        }
        html.Append("</tbody></table></body></html>");
        _html = html.ToString();
    }

    [Benchmark]
    public int ParseTransactionsFromHtml() =>
        DisclosureParsingHelper
            .ParseTransactionsFromHtml(
                _html,
                memberName: "John Doe",
                position: CongressPosition.Senator,
                filingDate: new DateOnly(2025, 6, 1),
                logger: NullLogger.Instance
            )
            .Count;
}
