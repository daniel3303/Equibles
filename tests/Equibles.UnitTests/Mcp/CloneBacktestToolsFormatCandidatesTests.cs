using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Mcp;

public class CloneBacktestToolsFormatCandidatesTests
{
    [Fact]
    public void FormatCandidates_IncludesResolutionHistoryHints()
    {
        var method = typeof(CloneBacktestTools).GetMethod(
            "FormatCandidates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var candidates = new List<InstitutionalHolderSearchMatch>
        {
            new()
            {
                Holder = new InstitutionalHolder { Name = "Example Capital", Cik = "1234567" },
                LatestReportDate = new DateOnly(2026, 3, 31),
                ReportedAum = 12_345_678_900L,
                PositionCount = 321,
            },
        };

        var result = (string)method!.Invoke(null, [candidates])!;

        result.Should().Contain("Example Capital (CIK 1234567");
        result.Should().Contain("latest 2026-03-31");
        result.Should().Contain("reported AUM $12,345,678,900");
        result.Should().Contain("positions 321");
    }
}
