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
}
