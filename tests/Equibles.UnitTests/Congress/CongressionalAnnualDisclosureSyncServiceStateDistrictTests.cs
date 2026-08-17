using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// The seat a member holds comes from the House Clerk's filing index, which is
/// the only source that states one — no Senate filing names the senator's
/// state. A member's reports therefore mix seat-bearing and seat-less rows, and
/// picking the wrong one either blanks a recorded seat or pins a stale district
/// across a redistricting.
/// </summary>
public class CongressionalAnnualDisclosureSyncServiceStateDistrictTests
{
    private static AnnualDisclosureReport Report(string stateDistrict, int year) =>
        new()
        {
            MemberName = "Jane Doe",
            Position = CongressPosition.Representative,
            StateDistrict = stateDistrict,
            Year = year,
            FiledDate = new DateOnly(year + 1, 5, 15),
            ReportId = $"doc-{year}",
            Lines = [],
        };

    [Fact]
    public void SelectStateDistrict_SingleReport_UsesItsSeat()
    {
        CongressionalAnnualDisclosureSyncService
            .SelectStateDistrict([Report("SC05", 2024)])
            .Should()
            .Be("SC05");
    }

    [Fact]
    public void SelectStateDistrict_Redistricted_TakesTheLatestFiling()
    {
        List<AnnualDisclosureReport> reports = [Report("TX35", 2021), Report("TX37", 2023)];

        CongressionalAnnualDisclosureSyncService.SelectStateDistrict(reports).Should().Be("TX37");
    }

    [Fact]
    public void SelectStateDistrict_LatestFilingStatesNoSeat_KeepsTheOneThatDoes()
    {
        // A member who moved to the Senate keeps filing, and those reports carry
        // no seat — the House seat already recorded must survive.
        List<AnnualDisclosureReport> reports = [Report("SC05", 2022), Report("", 2024)];

        CongressionalAnnualDisclosureSyncService.SelectStateDistrict(reports).Should().Be("SC05");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void SelectStateDistrict_NoReportStatesASeat_IsNull(string stateDistrict)
    {
        CongressionalAnnualDisclosureSyncService
            .SelectStateDistrict([Report(stateDistrict, 2024)])
            .Should()
            .BeNull();
    }

    [Fact]
    public void SelectStateDistrict_SeatIsPadded_KeepsTheClerksFormatting()
    {
        // "AK00" is an at-large seat, not a typo — the value is stored as the
        // Clerk publishes it and formatted for display elsewhere.
        CongressionalAnnualDisclosureSyncService
            .SelectStateDistrict([Report(" AK00 ", 2024)])
            .Should()
            .Be("AK00");
    }
}
