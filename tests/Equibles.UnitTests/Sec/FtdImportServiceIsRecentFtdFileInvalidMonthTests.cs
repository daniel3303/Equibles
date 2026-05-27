using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Symmetric sibling to <see cref="FtdImportServiceIsRecentFtdFileYearZeroTests"/>.
/// That pin covers the <c>year &gt;= 1</c> lower-bound gate; this one pins the
/// <c>month is &gt;= 1 and &lt;= 12</c> gate on the upper side. Without the gate,
/// a filename like <c>cnsfails202613a.zip</c> would fall through to
/// <c>new DateOnly(2026, 13, 1)</c>, whose constructor throws
/// <see cref="ArgumentOutOfRangeException"/> and crashes the FTD scrape cycle
/// instead of being silently rejected. The contract derived from the function
/// name + doc-comment is "validates and returns a bool, never throws".
/// </summary>
public class FtdImportServiceIsRecentFtdFileInvalidMonthTests
{
    [Fact]
    public void IsRecentFtdFile_MonthGreaterThanTwelve_ReturnsFalseWithoutThrowing()
    {
        var act = () => FtdImportService.IsRecentFtdFile("cnsfails202613a.zip");

        var result = act.Should().NotThrow().Subject;
        result.Should().BeFalse();
    }
}
