using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientMapToFilingDataTests
{
    // Lane B coverage pin: MapToFilingData is zero-coverage (the SecEdgarClientTests
    // header comment flags it) yet every SEC ingest sweep flows through it. It maps
    // six PARALLEL arrays positionally; a column swap (e.g. Form vs PrimaryDocument,
    // or FilingDate vs ReportDate) would silently corrupt every filing row. Pin each
    // field to its source column on a well-formed 2-entry input, plus the FormatCik
    // zero-pad baked into DocumentUrl. RecentFilings is internal → built via
    // reflection (the repo's established pattern for these private-static parsers).
    [Fact]
    public void MapToFilingData_WellFormedParallelArrays_MapsEachColumnToItsOwnField()
    {
        var asm = typeof(SecEdgarClient).Assembly;
        var recentType = asm.GetType("Equibles.Integrations.Sec.Models.Responses.RecentFilings")!;
        var recent = Activator.CreateInstance(recentType)!;

        void Set(string prop, List<string> values) =>
            recentType.GetProperty(prop)!.SetValue(recent, values);

        Set("AccessionNumber", ["0000320193-24-000010", "0000320193-24-000020"]);
        Set("FilingDate", ["2024-02-01", "2024-03-15"]);
        Set("ReportDate", ["2023-12-30", "2024-01-31"]);
        Set("Form", ["10-K", "8-K"]);
        Set("PrimaryDocument", ["aapl-20231230.htm", "ex991.htm"]);
        Set("PrimaryDocDescription", ["Annual report", "Press release"]);

        var map = typeof(SecEdgarClient).GetMethod(
            "MapToFilingData",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (List<FilingData>)map.Invoke(null, [recent, "320193"])!;

        result.Should().HaveCount(2);

        var first = result[0];
        first.Cik.Should().Be("320193");
        first.AccessionNumber.Should().Be("0000320193-24-000010");
        first.FilingDate.Should().Be(new DateOnly(2024, 2, 1));
        first.ReportDate.Should().Be(new DateOnly(2023, 12, 30));
        first.Form.Should().Be("10-K");
        first.PrimaryDocument.Should().Be("aapl-20231230.htm");
        first.Description.Should().Be("Annual report");
        first
            .DocumentUrl.Should()
            .Be("https://www.sec.gov/Archives/edgar/data/0000320193/0000320193-24-000010.txt");

        result[1].Form.Should().Be("8-K");
    }
}
