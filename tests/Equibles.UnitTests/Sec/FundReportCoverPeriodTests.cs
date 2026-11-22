using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

// N-CSR covers state their period in several markdown shapes; each case is the stored text of a real filing whose
// EDGAR index entry left the period blank.
public class FundReportCoverPeriodTests
{
    private static readonly DateOnly Filed = new(2025, 3, 6);

    [Theory]
    // Same line, numeric (0001193125-22-236310).
    [InlineData(
        "Date of fiscal year end: 12/31/2022\n\nDate of reporting period: 06/30/2022\n\n***\n\nItem 1 – Report to Stockholders",
        "2022-06-30"
    )]
    // A table cell (0001398344-23-005596).
    [InlineData(
        "| Date of fiscal year end: | December 31 |\n| --- | --- |\n| Date of reporting period: | December 31, 2022 |\n\nForm N-CSR is to be used",
        "2022-12-31"
    )]
    // The next paragraph, under a fiscal year end with no year (0001193125-22-259455).
    [InlineData(
        "Date of fiscal year end:\n\nJuly 31\n\nDate of reporting period:\n\nJuly 31, 2022\n\nForm N-CSR is to be used",
        "2022-07-31"
    )]
    // A heading (0001398344-22-018271).
    [InlineData(
        "### Date of Fiscal Year End: June 30\n\n### Date of Reporting Period: June 30, 2022\n\n| **Item 1.** | **Reports to Stockholders.** |",
        "2022-06-30"
    )]
    // A range ending in the period, joined by "through" (0001193125-23-023764).
    [InlineData(
        "Date of fiscal year end: November 30, 2022\n\nDate of reporting period: December 1, 2021 through November 30, 2022\n\nForm N‑CSR is to be used",
        "2022-11-30"
    )]
    // A range joined by a hyphen (0001398344-23-000298).
    [InlineData(
        "Date of fiscal year end: October 31\n\nDate of reporting period: November 1, 2021 - October 31, 2022\n\n| **Item 1.** |",
        "2022-10-31"
    )]
    // A two-digit year, the most common shape (0001580642-25-001621).
    [InlineData(
        "Date of reporting period: 12/31/24 **Item 1. Reports to Stockholders.** | (a)",
        "2024-12-31"
    )]
    // Several series filed together: the latest is the period EDGAR states (0001133228-22-007346).
    [InlineData(
        "Date of reporting period: **September 30, 2022 and October 31, 2022** Form N-CSR is to be used",
        "2022-10-31"
    )]
    public void Read_StatedCover_IsThePeriod(string cover, string period)
    {
        FundReportCoverPeriod.Read(cover, Filed).Should().Be(DateOnly.Parse(period));
    }

    // The label states no full date, so the next date in the report is never taken for it.
    [Fact]
    public void Read_LabelWithoutAFullDate_IsNull()
    {
        var cover =
            "Date of fiscal year end: December 31\n\nDate of reporting period: December 31\n\nAnnual Report June 30, 2022";

        FundReportCoverPeriod.Read(cover, Filed).Should().BeNull();
    }

    [Fact]
    public void Read_DateAfterTheFiling_IsNull()
    {
        FundReportCoverPeriod
            .Read("Date of reporting period: 06/30/2023", new DateOnly(2023, 3, 6))
            .Should()
            .BeNull();
    }

    // Only the cover counts: a label deep in the report, such as a quoted cover in an exhibit, is never read.
    [Fact]
    public void Read_LabelPastTheCover_IsNull()
    {
        var text = new string('x', 25_000) + "\nDate of reporting period: 06/30/2022";

        FundReportCoverPeriod.Read(text, Filed).Should().BeNull();
    }
}
