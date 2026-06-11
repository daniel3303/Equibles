using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pure-logic tests for the annual disclosure sync: the net-worth band
/// arithmetic, the latest-report-wins selection, and the in-place replacement
/// rule. The DB flow is covered by the integration tier.
/// </summary>
public class CongressionalAnnualDisclosureSyncServiceTests
{
    private static AnnualDisclosureLineItem Asset(long minimum, long maximum) =>
        new()
        {
            Kind = CongressionalDisclosureLineKind.Asset,
            Description = "Asset",
            RangeMinimum = minimum,
            RangeMaximum = maximum,
        };

    private static AnnualDisclosureLineItem Liability(long minimum, long maximum) =>
        new()
        {
            Kind = CongressionalDisclosureLineKind.Liability,
            Description = "Liability",
            RangeMinimum = minimum,
            RangeMaximum = maximum,
        };

    private static AnnualDisclosureReport Report(
        string member,
        int year,
        DateOnly filed,
        bool amendment,
        string reportId
    ) =>
        new()
        {
            MemberName = member,
            Position = CongressPosition.Representative,
            Year = year,
            FiledDate = filed,
            ReportId = reportId,
            IsAmendment = amendment,
        };

    [Fact]
    public void ComputeNetWorthBand_AssetsAndLiabilities_FollowsTheBandMethodology()
    {
        // Minimum = asset minimums − liability maximums; maximum = asset
        // maximums − liability minimums. The band brackets the truth from
        // both sides — never a point estimate.
        var (minimum, maximum) = CongressionalAnnualDisclosureSyncService.ComputeNetWorthBand([
            Asset(1_000_001, 5_000_000),
            Asset(15_001, 50_000),
            Liability(250_001, 500_000),
        ]);

        minimum.Should().Be(1_000_001 + 15_001 - 500_000);
        maximum.Should().Be(5_000_000 + 50_000 - 250_001);
    }

    [Fact]
    public void SelectLatestReports_AmendmentAndOriginal_KeepsTheLatestPerMemberYear()
    {
        var original = Report("Jane Doe", 2024, new DateOnly(2025, 5, 15), false, "1001");
        var amendment = Report("Jane Doe", 2024, new DateOnly(2025, 11, 4), true, "1002");
        var otherYear = Report("Jane Doe", 2023, new DateOnly(2024, 5, 15), false, "0901");
        var otherMember = Report("John Roe", 2024, new DateOnly(2025, 5, 15), false, "1003");

        var latest = CongressionalAnnualDisclosureSyncService.SelectLatestReports([
            amendment,
            original,
            otherYear,
            otherMember,
        ]);

        latest.Should().HaveCount(3);
        latest
            .Single(r => r.MemberName == "Jane Doe" && r.Year == 2024)
            .ReportId.Should()
            .Be("1002", "the amendment was filed later and replaces the original");
        latest.Should().Contain(otherYear).And.Contain(otherMember);
    }

    [Fact]
    public void SelectLatestReports_SameDayAmendment_BeatsTheOriginal()
    {
        var sameDay = new DateOnly(2025, 5, 15);
        var original = Report("Jane Doe", 2024, sameDay, false, "1001");
        var amendment = Report("Jane Doe", 2024, sameDay, true, "1002");

        var latest = CongressionalAnnualDisclosureSyncService.SelectLatestReports([
            amendment,
            original,
        ]);

        latest.Should().ContainSingle().Which.ReportId.Should().Be("1002");
    }

    [Theory]
    [InlineData("1001", "2025-05-15", false, false)] // same source report → no-op
    [InlineData("1002", "2025-11-04", false, true)] // later filing replaces
    [InlineData("1002", "2025-01-01", false, false)] // earlier filing never replaces
    [InlineData("1002", "2025-05-15", true, true)] // same-day amendment replaces
    [InlineData("1002", "2025-05-15", false, false)] // same-day non-amendment does not
    public void ShouldReplace_AppliesTheLatestFiledWinsRule(
        string incomingReportId,
        string incomingFiled,
        bool incomingIsAmendment,
        bool expected
    )
    {
        var existing = new CongressionalAnnualDisclosure
        {
            ReportId = "1001",
            FiledDate = new DateOnly(2025, 5, 15),
            Year = 2024,
        };
        var incoming = Report(
            "Jane Doe",
            2024,
            DateOnly.Parse(incomingFiled),
            incomingIsAmendment,
            incomingReportId
        );

        CongressionalAnnualDisclosureSyncService
            .ShouldReplace(existing, incoming)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("Member", true)]
    [InlineData("Member-elect", true)]
    [InlineData("Congressional Candidate", false)]
    [InlineData("Officer or Employee", false)]
    [InlineData(null, true)]
    public void IsMemberFilerStatus_GatesNonMemberReports(string status, bool expected)
    {
        HouseAnnualReportClient.IsMemberFilerStatus(status).Should().Be(expected);
    }
}
