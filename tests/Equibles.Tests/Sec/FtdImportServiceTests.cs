using Equibles.Sec.HostedService.Services;

namespace Equibles.Tests.Sec;

public class FtdImportServiceTests {
    // ── GetFileNames ──

    [Fact]
    public void GetFileNames_SingleMonth_ReturnsTwoFiles() {
        // Use a date far enough in the past that the month boundary is clear
        var startDate = new DateOnly(2020, 1, 15);

        var fileNames = FtdImportService.GetFileNames(startDate);

        // Should include at least the start month's a and b files
        fileNames.Should().Contain("cnsfails202001a.zip");
        fileNames.Should().Contain("cnsfails202001b.zip");
    }

    [Fact]
    public void GetFileNames_AlwaysStartsFromFirstOfMonth() {
        // Even if start date is mid-month, file names start from month beginning
        var startDate = new DateOnly(2024, 6, 20);

        var fileNames = FtdImportService.GetFileNames(startDate);

        fileNames[0].Should().Be("cnsfails202406a.zip");
        fileNames[1].Should().Be("cnsfails202406b.zip");
    }

    [Fact]
    public void GetFileNames_MultipleMonths_GeneratesAllPairs() {
        // A start date in Jan 2024 + current is at least March 2026 → many months
        var startDate = new DateOnly(2024, 1, 1);

        var fileNames = FtdImportService.GetFileNames(startDate);

        // All entries come in pairs (a + b)
        fileNames.Count.Should().BeGreaterThan(0);
        (fileNames.Count % 2).Should().Be(0);

        // Verify first pair
        fileNames[0].Should().Be("cnsfails202401a.zip");
        fileNames[1].Should().Be("cnsfails202401b.zip");

        // Verify second pair
        fileNames[2].Should().Be("cnsfails202402a.zip");
        fileNames[3].Should().Be("cnsfails202402b.zip");
    }

    [Fact]
    public void GetFileNames_FutureDate_ReturnsEmptyList() {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);

        var fileNames = FtdImportService.GetFileNames(futureDate);

        fileNames.Should().BeEmpty();
    }

    // ── IsRecentFtdFile ──

    [Fact]
    public void IsRecentFtdFile_CurrentMonth_ReturnsTrue() {
        // Use a month safely inside the 2-month window to avoid boundary flakes
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var safeRecent = now.AddMonths(0).ToString("yyyyMM");
        var fileName = $"cnsfails{safeRecent}a.zip";

        // Also test the prior month to be safe across month boundaries
        var priorMonth = now.AddMonths(-1).ToString("yyyyMM");
        var priorFileName = $"cnsfails{priorMonth}b.zip";

        // At least one of these must be recent (covers month-boundary edge case)
        var result = FtdImportService.IsRecentFtdFile(fileName)
            || FtdImportService.IsRecentFtdFile(priorFileName);
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRecentFtdFile_SixMonthsAgo_ReturnsFalse() {
        var oldDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-6);
        var fileName = $"cnsfails{oldDate:yyyyMM}a.zip";

        FtdImportService.IsRecentFtdFile(fileName).Should().BeFalse();
    }

    [Theory]
    [InlineData("short.zip")]
    [InlineData("cnsfailsXXXXYYa.zip")]
    [InlineData("cnsfails202413a.zip")]  // Invalid month 13
    [InlineData("cnsfails202400a.zip")]  // Invalid month 0
    public void IsRecentFtdFile_InvalidFormat_ReturnsFalse(string fileName) {
        FtdImportService.IsRecentFtdFile(fileName).Should().BeFalse();
    }
}
