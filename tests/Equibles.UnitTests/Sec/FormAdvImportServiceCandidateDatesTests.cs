using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the order the importer probes SEC snapshots: newest first (so it stops at the most recent
/// published file), the first of each month (the SEC's filename convention), and a bounded window
/// so a permanently-missing file can't make the probe walk back indefinitely.
/// </summary>
public class FormAdvImportServiceCandidateDatesTests
{
    [Fact]
    public void GetCandidateFileDates_AreFirstOfMonth_NewestFirst_AndBounded()
    {
        var dates = FormAdvImportService.GetCandidateFileDates().ToList();

        dates.Should().HaveCount(4);
        dates.Should().OnlyContain(d => d.Day == 1);
        dates.Should().BeInDescendingOrder();

        var thisMonth = new DateOnly(
            DateOnly.FromDateTime(DateTime.UtcNow).Year,
            DateOnly.FromDateTime(DateTime.UtcNow).Month,
            1
        );
        dates[0].Should().Be(thisMonth);
        dates[3].Should().Be(thisMonth.AddMonths(-3));
    }
}
