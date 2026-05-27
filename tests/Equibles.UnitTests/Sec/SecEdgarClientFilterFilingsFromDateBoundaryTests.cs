using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientFilterFilingsFromDateBoundaryTests
{
    // Sibling to the existing toDate inclusive-boundary pin. A "filings from
    // <fromDate>" query must treat the start date as INCLUSIVE — a caller
    // asking for filings since 2024-01-01 relies on a filing filed exactly
    // on 2024-01-01 being returned. The classic regression is an exclusive
    // lower bound (`> fromDate`), which would silently drop the boundary
    // day on every backfill cycle. Pin: boundary filing retained, day-
    // before excluded.
    [Fact]
    public void FilterFilings_FilingDatedExactlyOnFromDate_IsRetained()
    {
        var fromDate = new DateOnly(2024, 1, 1);
        var dayBefore = new FilingData
        {
            AccessionNumber = "0000320193-23-000900",
            FilingDate = fromDate.AddDays(-1),
            Form = "10-K",
        };
        var onBoundary = new FilingData
        {
            AccessionNumber = "0000320193-24-000001",
            FilingDate = fromDate,
            Form = "10-K",
        };

        var filter = typeof(SecEdgarClient).GetMethod(
            "FilterFilings",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result =
            (List<FilingData>)
                filter.Invoke(
                    null,
                    [
                        new List<FilingData> { dayBefore, onBoundary },
                        null,
                        (DateOnly?)fromDate,
                        null,
                    ]
                );

        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("0000320193-24-000001");
    }
}
