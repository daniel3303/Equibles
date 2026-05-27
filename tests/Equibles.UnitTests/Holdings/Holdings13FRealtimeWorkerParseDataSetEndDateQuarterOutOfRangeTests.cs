using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Symmetric sibling to
/// <see cref="Holdings13FRealtimeWorkerParseDataSetEndDateYearZeroTests"/>. That
/// pin protects the old-format <c>year &gt;= 1</c> gate; this one protects the
/// <c>quarter is &gt;= 1 and &lt;= 4</c> upper-bound gate. Without that gate,
/// a filename like <c>2023q5_form13f.zip</c> would compute
/// <c>endMonth = 5 * 3 = 15</c> and fall through to <c>new DateOnly(year, 15, …)</c>,
/// which throws <see cref="ArgumentOutOfRangeException"/> and crashes the worker
/// startup instead of being silently rejected as malformed.
/// </summary>
public class Holdings13FRealtimeWorkerParseDataSetEndDateQuarterOutOfRangeTests
{
    [Fact]
    public void ParseDataSetEndDate_OldFormatQuarterFive_ReturnsNullInsteadOfThrowing()
    {
        var act = () => Holdings13FRealtimeWorker.ParseDataSetEndDate("2023q5_form13f.zip");

        var result = act.Should().NotThrow().Subject;
        result.Should().BeNull();
    }
}
