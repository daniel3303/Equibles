using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsScreenerControllerBuildCsvColumnAlignmentTests
{
    private static readonly MethodInfo BuildCsvMethod =
        typeof(HoldingsScreenerController).GetMethod(
            "BuildCsv",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // The two existing BuildCsv pins both check column 11 (PercentOfFloat)
    // in isolation. Neither verifies the column ORDERING — the contract
    // that header N maps to data column N for every column. The two are
    // constructed in separate code blocks: a `headers` array literal, and
    // a `csvRows` Select that produces a new[] in a particular order. A
    // maintainer who reorders the headers (e.g. "promote CurrentValue
    // above CurrentFilerCount because monetary value is the more
    // important screen") without reordering the data array — or vice
    // versa — would compile cleanly, pass both existing pins (PercentOf
    // Float still lands at column 11), and silently emit CSVs where
    // every column carries the wrong header.
    //
    // The downstream pipeline (data-science notebooks, Excel imports,
    // BI tooling) reads the file by header name when it's available — so
    // a misaligned export means filer counts get logged as monetary
    // values and vice versa: numeric scale alone wouldn't flag it.
    //
    // Adversarial input: a single row with DISTINCT identifiable values
    // in every numeric column. The data array's column count matches
    // the header count, so a column-shift regression would surface as
    // a value-at-wrong-column assertion failure - not as a row-length
    // mismatch the existing pin's count assertion catches.
    //
    // Pin both axes:
    //   • The header line is the exact 12-column literal in production.
    //   • Each data column at index N carries the value the corresponding
    //     header at index N promises.
    [Fact]
    public void BuildCsv_DistinctValuesPerColumn_HeaderAndDataAlignByIndex()
    {
        var rows = new List<ScreenerRow>
        {
            new ScreenerRow
            {
                Ticker = "TICK",
                Name = "TestCo",
                IndustryName = "TestIndustry",
                CurrentFilerCount = 100,
                PreviousFilerCount = 80,
                CurrentValue = 5_000_000L,
                PreviousValue = 4_000_000L,
                NewFilerCount = 3,
                SoldOutFilerCount = 1,
                PercentOfFloat = 0.5,
            },
        };

        var csv = (string)BuildCsvMethod.Invoke(null, [rows]);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);

        lines[0]
            .TrimEnd('\r')
            .Should()
            .Be(
                "Ticker,Name,Industry,CurrentFilerCount,PreviousFilerCount,DeltaFilerCount,CurrentValue,PreviousValue,DeltaValue,NewFilerCount,SoldOutFilerCount,PercentOfFloat"
            );

        var dataCells = lines[1].TrimEnd('\r').Split(',');
        dataCells.Should().HaveCount(12);
        dataCells[0].Should().Be("TICK");
        dataCells[1].Should().Be("TestCo");
        dataCells[2].Should().Be("TestIndustry");
        dataCells[3].Should().Be("100");
        dataCells[4].Should().Be("80");
        dataCells[5].Should().Be("20"); // DeltaFilerCount = 100 - 80
        dataCells[6].Should().Be("5000000");
        dataCells[7].Should().Be("4000000");
        dataCells[8].Should().Be("1000000"); // DeltaValue = 5_000_000 - 4_000_000
        dataCells[9].Should().Be("3");
        dataCells[10].Should().Be("1");
        dataCells[11].Should().Be("0.5000");
    }
}
