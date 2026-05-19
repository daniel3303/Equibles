using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientFilterFilingsTests
{
    // Adversarial: a "filings up to <toDate>" query must treat the end date as
    // INCLUSIVE — a caller asking for filings through 2024-12-31 reasonably relies
    // on a filing filed exactly on 2024-12-31 being returned. The classic defect
    // here is an exclusive upper bound (`< toDate`), which would silently drop the
    // boundary day. Pin: boundary filing retained, day-after excluded.
    [Fact]
    public void FilterFilings_FilingDatedExactlyOnToDate_IsRetained()
    {
        var toDate = new DateOnly(2024, 12, 31);
        var onBoundary = new FilingData
        {
            AccessionNumber = "0000320193-24-000099",
            FilingDate = toDate,
            Form = "10-K",
        };
        var dayAfter = new FilingData
        {
            AccessionNumber = "0000320193-25-000001",
            FilingDate = toDate.AddDays(1),
            Form = "10-K",
        };

        var filter = typeof(SecEdgarClient).GetMethod(
            "FilterFilings",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result =
            (List<FilingData>)
                filter.Invoke(
                    null,
                    [new List<FilingData> { onBoundary, dayAfter }, null, null, (DateOnly?)toDate]
                )!;

        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("0000320193-24-000099");
    }
}
