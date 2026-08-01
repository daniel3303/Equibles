using Equibles.Sec.HostedService.Services;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The retired-CUSIP sweep resumes from a frontier stored in BackfillState's DateTime
/// column, so a fortnightly archive file name has to survive the round trip through a
/// timestamp. If it does not, the sweep either re-downloads years of archive every cycle
/// or silently stops advancing.
/// </summary>
public class FtdImportServiceAliasSweepCursorTests
{
    [Theory]
    [InlineData("cnsfails201706b.zip")]
    [InlineData("cnsfails202212a.zip")]
    [InlineData("cnsfails202212b.zip")]
    [InlineData("cnsfails202601a.zip")]
    public void AFileNameSurvivesTheFrontierRoundTrip(string fileName)
    {
        var frontier = FtdImportService.FrontierOf(fileName);

        FtdImportService.FileNameOf(frontier).Should().Be(fileName);
    }

    [Fact]
    public void TheTwoHalvesOfOneMonthStayOrdered()
    {
        // Both halves share a month, so the frontier must separate them or the sweep
        // cannot tell which of the two it already read.
        var first = FtdImportService.FrontierOf("cnsfails202212a.zip");
        var second = FtdImportService.FrontierOf("cnsfails202212b.zip");

        first.Should().BeBefore(second);
    }

    [Fact]
    public void TheFrontierAdvancesAcrossMonths()
    {
        var december = FtdImportService.FrontierOf("cnsfails202212b.zip");
        var january = FtdImportService.FrontierOf("cnsfails202301a.zip");

        december.Should().BeBefore(january);
    }

    [Fact]
    public void EveryGeneratedFileNameRoundTrips()
    {
        // The cursor is looked up by IndexOf against this very list, so a spelling the
        // round trip cannot reproduce would strand the sweep.
        var all = FtdImportService.GetFileNames(new DateOnly(2017, 6, 1));

        all.Should()
            .OnlyContain(f => FtdImportService.FileNameOf(FtdImportService.FrontierOf(f)) == f);
    }
}
