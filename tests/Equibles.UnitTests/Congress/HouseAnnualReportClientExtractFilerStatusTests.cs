using Equibles.Congress.HostedService.Services;
using static Equibles.Congress.HostedService.Services.HouseAnnualReportClient;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Contract (HouseAnnualReportClient.ExtractFilerStatus): the preamble renders
/// the filer status as a "Status:" label token followed by its value, before
/// the first schedule header. The value can be more than one word — the doc
/// comment names "Status: Congressional Candidate" — so the method must return
/// the WHOLE value, skipping unrelated preamble lines on the way. A regression
/// that returned only the first token after the label (e.g. `line[1].Text`)
/// would drop "Candidate" and surface here.
/// </summary>
public class HouseAnnualReportClientExtractFilerStatusTests
{
    private static List<ScheduleToken> Tokens(params (string text, double left)[] words) =>
        words.Select(w => new ScheduleToken(w.text, w.left)).ToList();

    [Fact]
    public void ExtractFilerStatus_MultiWordStatusBeforeSchedule_ReturnsFullStatus()
    {
        var result = HouseAnnualReportClient.ExtractFilerStatus([
            Tokens(("Name:", 25), ("Jane", 60), ("Doe", 85)),
            Tokens(("Status:", 25), ("Congressional", 70), ("Candidate", 160)),
            Tokens(("S", 22), ("A:", 92)),
        ]);

        result.Should().Be("Congressional Candidate");
    }
}
