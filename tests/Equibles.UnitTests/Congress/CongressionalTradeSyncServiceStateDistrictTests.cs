using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// The trade lane records a member's seat from the House Clerk's filing index, the only source
/// that states one. A member's transactions mix seat-bearing House rows with seat-less Senate
/// rows, so the selection has to survive the blanks rather than take the last row it sees.
/// </summary>
public class CongressionalTradeSyncServiceStateDistrictTests
{
    private static DisclosureTransaction Transaction(string stateDistrict, DateOnly filed) =>
        new()
        {
            MemberName = "Jane Doe",
            Position = CongressPosition.Representative,
            StateDistrict = stateDistrict,
            FilingDate = filed,
            TransactionDate = filed.AddDays(-10),
            TransactionType = CongressTransactionType.Purchase,
            AmountFrom = 1_001,
            AmountTo = 15_000,
        };

    [Fact]
    public void SelectStateDistrict_SingleFiling_UsesItsSeat()
    {
        CongressionalTradeSyncService
            .SelectStateDistrict([Transaction("SC05", new DateOnly(2026, 3, 1))])
            .Should()
            .Be("SC05");
    }

    [Fact]
    public void SelectStateDistrict_Redistricted_TakesTheLatestFiling()
    {
        List<DisclosureTransaction> transactions =
        [
            Transaction("TX35", new DateOnly(2022, 6, 1)),
            Transaction("TX37", new DateOnly(2024, 6, 1)),
        ];

        CongressionalTradeSyncService.SelectStateDistrict(transactions).Should().Be("TX37");
    }

    [Fact]
    public void SelectStateDistrict_LatestFilingStatesNoSeat_KeepsTheOneThatDoes()
    {
        // A member who moved to the Senate keeps trading, and those rows carry no seat. Taking
        // the last row outright would blank the House seat already on file.
        List<DisclosureTransaction> transactions =
        [
            Transaction("SC05", new DateOnly(2023, 6, 1)),
            Transaction("", new DateOnly(2026, 6, 1)),
        ];

        CongressionalTradeSyncService.SelectStateDistrict(transactions).Should().Be("SC05");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void SelectStateDistrict_NoFilingStatesASeat_IsNull(string stateDistrict)
    {
        CongressionalTradeSyncService
            .SelectStateDistrict([Transaction(stateDistrict, new DateOnly(2026, 3, 1))])
            .Should()
            .BeNull();
    }

    [Fact]
    public void SelectStateDistrict_PaddedSeat_KeepsTheClerksFormatting()
    {
        // "AK00" is an at-large seat, not a typo — stored as published, formatted for display
        // elsewhere.
        CongressionalTradeSyncService
            .SelectStateDistrict([Transaction(" AK00 ", new DateOnly(2026, 3, 1))])
            .Should()
            .Be("AK00");
    }
}
