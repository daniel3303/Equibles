using Equibles.Congress.Data;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// The stored seat is the Clerk's key ("SC05"), not a label. Formatting it is a
/// display concern, and district 00 is an at-large state rather than a district
/// numbered zero.
/// </summary>
public class CongressSeatTests
{
    [Theory]
    [InlineData("SC05", "SC-5")]
    [InlineData("TX37", "TX-37")]
    [InlineData("NY14", "NY-14")]
    [InlineData("sc05", "SC-5")]
    [InlineData(" SC05 ", "SC-5")]
    public void Format_DistrictSeat_DropsThePadding(string stored, string expected) =>
        CongressSeat.Format(stored).Should().Be(expected);

    [Theory]
    [InlineData("AK00")]
    [InlineData("WY00")]
    public void Format_AtLargeSeat_SaysSoRatherThanDistrictZero(string stored) =>
        CongressSeat.Format(stored).Should().Be($"{stored[..2]} At-Large");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_NoSeat_IsNull(string stored) =>
        CongressSeat.Format(stored).Should().BeNull();

    [Theory]
    [InlineData("PUERTO RICO")]
    [InlineData("SC-5")]
    [InlineData("SC005")]
    public void Format_UnrecognisedShape_IsShownAsStored(string stored) =>
        CongressSeat.Format(stored).Should().Be(stored);

    [Theory]
    [InlineData("SC05", "SC", 5)]
    [InlineData("TX37", "TX", 37)]
    [InlineData("ny3", "NY", 3)]
    [InlineData(" CA12 ", "CA", 12)]
    [InlineData("AK00", "AK", 0)]
    [InlineData("DC00", "DC", 0)]
    [InlineData("WYAL", "WY", 0)]
    [InlineData("mtal", "MT", 0)]
    public void TryParse_SeatKey_ReturnsStateAndDistrict(
        string stored,
        string expectedState,
        short expectedDistrict
    )
    {
        CongressSeat.TryParse(stored, out var state, out var district).Should().BeTrue();

        state.Should().Be(expectedState);
        district.Should().Be(expectedDistrict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SC")]
    [InlineData("SC-5")]
    [InlineData("SC005")]
    [InlineData("S05")]
    [InlineData("PUERTO RICO")]
    public void TryParse_UnrecognisedShape_ReturnsFalse(string stored)
    {
        CongressSeat.TryParse(stored, out var state, out _).Should().BeFalse();

        state.Should().BeNull();
    }

    [Fact]
    public void Format_AtLargeLetters_SaysAtLarge() =>
        CongressSeat.Format("WYAL").Should().Be("WY At-Large");
}
